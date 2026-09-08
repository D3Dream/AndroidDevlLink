using System.IO;

namespace AndroidDevLink.Models;

public sealed record AndroidFileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long? Size,
    DateTimeOffset? ModifiedTime,
    string Permissions,
    bool IsSymbolicLink)
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3g2", ".3gp", ".aac", ".aiff", ".alac", ".amr", ".ape", ".asf", ".avi",
        ".avif", ".bmp", ".divx", ".dng", ".flac", ".flv", ".gif", ".heic", ".heif",
        ".jpeg", ".jpg", ".m2ts", ".m4a", ".m4v", ".mid", ".midi", ".mkv", ".mov",
        ".mp3", ".mp4", ".mpeg", ".mpg", ".mts", ".oga", ".ogg", ".opus", ".png",
        ".raw", ".rmvb", ".ts", ".wav", ".webm", ".webp", ".wma", ".wmv"
    };

    public bool IsMediaFile => !IsDirectory && !IsSymbolicLink && IsMediaFileName(Name);

    public static bool IsMediaFileName(string fileName) =>
        MediaExtensions.Contains(Path.GetExtension(fileName));

    public string TypeDisplay => IsDirectory
        ? "文件夹"
        : IsSymbolicLink
            ? "符号链接"
            : string.IsNullOrWhiteSpace(Path.GetExtension(Name))
                ? "文件"
                : $"{Path.GetExtension(Name).TrimStart('.').ToUpperInvariant()} 文件";

    public string SizeDisplay => IsDirectory || Size is null ? "—" : FormatSize(Size.Value);

    public string ModifiedTimeDisplay => ModifiedTime?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} B" : $"{value:0.##} {units[unitIndex]}";
    }
}
