using System.Globalization;

namespace SplitzNJoinz.ViewModels;

internal static class Format
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

    /// <summary>"1.5 GB" style (1 KB = 1024 bytes), matching the chunk size units.</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return bytes == 1 ? "1 byte" : $"{bytes:N0} bytes";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value < 10 ? "0.##" : value < 100 ? "0.#" : "0", CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string Elapsed(TimeSpan time) =>
        time.TotalSeconds < 1 ? "under a second"
        : time.TotalMinutes < 1 ? $"{time.TotalSeconds:0.#} s"
        : $"{(int)time.TotalMinutes} min {time.Seconds} s";
}
