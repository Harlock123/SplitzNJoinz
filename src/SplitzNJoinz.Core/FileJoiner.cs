namespace SplitzNJoinz.Core;

/// <summary>A complete run of chunks, <c>name.001</c> to <c>name.N</c>, ready to join.</summary>
public sealed record ChunkSet(string Directory, string OriginalFileName, IReadOnlyList<string> ChunkPaths, long TotalBytes)
{
    /// <summary>Where the joined file goes by default: beside the chunks, under its original name.</summary>
    public string DefaultOutputPath => Path.Combine(Directory, OriginalFileName);
}

public static class FileJoiner
{
    /// <summary>Finds every chunk that belongs with <paramref name="anyChunkPath"/> (which can be any one of them).</summary>
    /// <exception cref="ArgumentException">The file name doesn't end in a chunk number.</exception>
    /// <exception cref="InvalidDataException">Chunks are missing, or two files claim the same number.</exception>
    public static ChunkSet Discover(string anyChunkPath)
    {
        var fullPath = Path.GetFullPath(anyChunkPath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The chunk file doesn't exist.", fullPath);
        if (!ChunkNaming.TryParse(Path.GetFileName(fullPath), out var originalName, out _))
            throw new ArgumentException(
                $"{Path.GetFileName(fullPath)} doesn't look like a chunk; chunk names end in a number, like name.ext.001.",
                nameof(anyChunkPath));

        var directory = Path.GetDirectoryName(fullPath)!;
        var chunks = ChunkNaming.FindChunks(directory, originalName);
        if (chunks.TryGetValue(0, out var zero))
            throw new InvalidDataException($"{Path.GetFileName(zero)} is numbered 0, but chunks are numbered from 1.");
        var last = chunks.Keys.Max();
        if (chunks.Count != last) // (numbers are distinct and 1..last, so equal counts mean none missing)
            throw new InvalidDataException(
                $"Chunk(s) {Summarise(Enumerable.Range(1, last).Where(n => !chunks.ContainsKey(n)))} of {originalName} are missing from {directory}; found {chunks.Count} of {last}.");

        var paths = chunks.Values.ToList();
        var total = paths.Sum(p => new FileInfo(p).Length);
        return new ChunkSet(directory, originalName, paths, total);
    }

    /// <summary>
    /// Writes the chunks, in order, into <paramref name="outputPath"/>. The file is built under a temporary name and
    /// only renamed into place once complete, so an existing file is never left half-overwritten.
    /// </summary>
    /// <exception cref="IOException">The output exists and <paramref name="overwrite"/> is false, or it is one of the chunks.</exception>
    public static async Task JoinAsync(
        ChunkSet set,
        string outputPath,
        bool overwrite = false,
        IProgress<ChunkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        outputPath = Path.GetFullPath(outputPath);
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (set.ChunkPaths.Any(p => string.Equals(Path.GetFullPath(p), outputPath, comparison)))
            throw new IOException("The joined file can't be written over one of its own chunks.");
        if (File.Exists(outputPath) && !overwrite)
            throw new IOException($"{Path.GetFileName(outputPath)} already exists. Allow overwriting, or choose another name.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var temp = outputPath + ".joining";
        var buffer = new byte[FileSplitter.BufferSize];
        long done = 0;
        try
        {
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                             FileSplitter.BufferSize, FileOptions.Asynchronous))
            {
                for (var i = 0; i < set.ChunkPaths.Count; i++)
                {
                    await using var input = new FileStream(set.ChunkPaths[i], FileMode.Open, FileAccess.Read, FileShare.Read,
                        FileSplitter.BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        done += read;
                        progress?.Report(new ChunkProgress(done, set.TotalBytes, i + 1, set.ChunkPaths.Count));
                    }
                }
            }
            File.Move(temp, outputPath, overwrite);
        }
        catch
        {
            FileSplitter.TryDelete(temp);
            throw;
        }
    }

    /// <summary>"3, 5, 7-12" style list of chunk numbers.</summary>
    private static string Summarise(IEnumerable<int> numbers)
    {
        var parts = new List<string>();
        int? start = null, end = null;
        foreach (var n in numbers)
        {
            if (end == n - 1) { end = n; continue; }
            if (start is not null) parts.Add(start == end ? $"{start}" : $"{start}-{end}");
            if (parts.Count == 10) { parts.Add("..."); return string.Join(", ", parts); }
            start = end = n;
        }
        if (start is not null) parts.Add(start == end ? $"{start}" : $"{start}-{end}");
        return string.Join(", ", parts);
    }
}
