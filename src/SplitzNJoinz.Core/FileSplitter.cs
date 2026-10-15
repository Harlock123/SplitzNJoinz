namespace SplitzNJoinz.Core;

/// <summary>How far a split or join has got: bytes copied so far and the chunk being worked on (1-based).</summary>
public readonly record struct ChunkProgress(long BytesDone, long TotalBytes, int Chunk, int ChunkCount)
{
    public double Fraction => TotalBytes == 0 ? 1 : (double)BytesDone / TotalBytes;
}

public sealed record SplitResult(IReadOnlyList<string> ChunkPaths, long TotalBytes);

public static class FileSplitter
{
    /// <summary>A guard against a typo (bytes instead of MB) producing millions of files.</summary>
    public const int MaxChunks = 10_000;

    internal const int BufferSize = 1 << 20;

    /// <summary>How many chunks a file of <paramref name="fileBytes"/> needs. An empty file still gets one (empty) chunk.</summary>
    public static long ChunkCount(long fileBytes, long maxChunkBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkBytes);
        return Math.Max(1, (fileBytes + maxChunkBytes - 1) / maxChunkBytes);
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into chunks of at most <paramref name="maxChunkBytes"/> each, written to
    /// <paramref name="outputDirectory"/> as <c>name.001</c>, <c>name.002</c>, ... The source is left untouched.
    /// </summary>
    /// <param name="replaceExisting">
    /// Chunks of this file already in the output folder are deleted first (all of them, so a leftover <c>.009</c> from
    /// an earlier, finer split can't sneak into a later join). When false, finding any is an error.
    /// </param>
    /// <remarks>On failure or cancellation the chunks written so far are deleted.</remarks>
    public static async Task<SplitResult> SplitAsync(
        string sourcePath,
        string outputDirectory,
        long maxChunkBytes,
        bool replaceExisting = false,
        IProgress<ChunkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkBytes);
        var source = new FileInfo(sourcePath);
        if (!source.Exists) throw new FileNotFoundException("The file to split doesn't exist.", sourcePath);
        Directory.CreateDirectory(outputDirectory);

        var total = source.Length;
        var count = ChunkCount(total, maxChunkBytes);
        if (count > MaxChunks)
            throw new ArgumentException(
                $"That chunk size would make {count:N0} chunks; the limit is {MaxChunks:N0}. Choose a larger chunk size.",
                nameof(maxChunkBytes));
        var chunkCount = (int)count;

        var existing = ChunkNaming.FindChunks(outputDirectory, source.Name);
        if (existing.Count > 0)
        {
            if (!replaceExisting)
                throw new IOException(
                    $"{existing.Count} chunk file(s) of {source.Name} are already in {outputDirectory}. " +
                    "Allow replacing them, or choose another output folder.");
            foreach (var path in existing.Values) File.Delete(path);
        }

        var width = ChunkNaming.DigitWidth(chunkCount);
        var written = new List<string>(chunkCount);
        var buffer = new byte[BufferSize];
        long done = 0;
        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            for (var chunk = 1; chunk <= chunkCount; chunk++)
            {
                var path = Path.Combine(outputDirectory, ChunkNaming.ChunkFileName(source.Name, chunk, width));
                written.Add(path);
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.Asynchronous);
                var remaining = Math.Min(maxChunkBytes, total - done);
                while (remaining > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                    if (read == 0) throw new EndOfStreamException($"{source.Name} got shorter while it was being split.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    remaining -= read;
                    done += read;
                    progress?.Report(new ChunkProgress(done, total, chunk, chunkCount));
                }
                if (total == 0) progress?.Report(new ChunkProgress(0, 0, chunk, chunkCount));
            }
        }
        catch
        {
            foreach (var path in written) TryDelete(path);
            throw;
        }
        return new SplitResult(written, total);
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
