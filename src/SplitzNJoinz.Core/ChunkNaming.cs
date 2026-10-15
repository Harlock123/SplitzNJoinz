using System.Globalization;

namespace SplitzNJoinz.Core;

/// <summary>
/// Chunks are named after the original file with the chunk number appended:
/// <c>movie.mkv</c> splits into <c>movie.mkv.001</c>, <c>movie.mkv.002</c>, ...
/// Numbers start at 1 and are zero-padded to at least three digits (more when there are 1000+ chunks).
/// </summary>
public static class ChunkNaming
{
    public const int MinDigits = 3;

    /// <summary>The number of digits used for the chunk numbers of a split into <paramref name="chunkCount"/> chunks.</summary>
    public static int DigitWidth(int chunkCount) =>
        Math.Max(MinDigits, chunkCount.ToString(CultureInfo.InvariantCulture).Length);

    public static string ChunkFileName(string originalFileName, int number, int width) =>
        $"{originalFileName}.{number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}";

    /// <summary>
    /// Splits a chunk's file name into the original file name and the chunk number,
    /// e.g. <c>movie.mkv.007</c> → (<c>movie.mkv</c>, 7). False when the name doesn't end in <c>.digits</c>.
    /// </summary>
    public static bool TryParse(string chunkFileName, out string originalFileName, out int number)
    {
        originalFileName = "";
        number = 0;
        var dot = chunkFileName.LastIndexOf('.');
        if (dot <= 0 || dot == chunkFileName.Length - 1) return false;
        var digits = chunkFileName.AsSpan(dot + 1);
        foreach (var c in digits)
            if (c is < '0' or > '9') return false;
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;
        originalFileName = chunkFileName[..dot];
        return true;
    }

    /// <summary>Every chunk of <paramref name="originalFileName"/> in <paramref name="directory"/>, by number.</summary>
    /// <exception cref="InvalidDataException">Two files carry the same number (e.g. <c>a.bin.1</c> and <c>a.bin.001</c>).</exception>
    public static SortedDictionary<int, string> FindChunks(string directory, string originalFileName)
    {
        var chunks = new SortedDictionary<int, string>();
        if (!Directory.Exists(directory)) return chunks;
        foreach (var path in Directory.EnumerateFiles(directory, originalFileName + ".*"))
        {
            if (!TryParse(Path.GetFileName(path), out var name, out var number) || name != originalFileName) continue;
            if (!chunks.TryAdd(number, path))
                throw new InvalidDataException(
                    $"Two files claim to be chunk {number}: {Path.GetFileName(chunks[number])} and {Path.GetFileName(path)}.");
        }
        return chunks;
    }
}
