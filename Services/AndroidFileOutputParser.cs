using System.Globalization;
using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public static class AndroidFileOutputParser
{
    public static IReadOnlyList<AndroidFileEntry> ParseDirectory(string directoryPath, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(output);

        List<AndroidFileEntry> entries = [];
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseLine(NormalizePath(directoryPath), line.Trim(), out AndroidFileEntry entry))
            {
                entries.Add(entry);
            }
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseLine(string directoryPath, string line, out AndroidFileEntry entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Android toybox ls -la 的稳定前缀：权限 链接数 用户 用户组 大小 日期 时间 名称。
        string[] fields = line.Split(' ', 8, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 8 || fields[0].Length < 10)
        {
            return false;
        }

        char kind = fields[0][0];
        if (kind is not ('d' or '-' or 'l'))
        {
            return false;
        }

        string nameField = fields[7];
        bool isSymbolicLink = kind == 'l';
        string name = isSymbolicLink
            ? nameField.Split(" -> ", 2, StringSplitOptions.None)[0]
            : nameField;
        if (name is "." or ".." || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        long? size = long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedSize)
            ? parsedSize
            : null;
        DateTimeOffset? modifiedTime = TryParseModifiedTime(fields[5], fields[6]);
        bool isDirectory = kind == 'd';
        string fullPath = CombineAndroidPath(directoryPath, name);

        entry = new AndroidFileEntry(
            name,
            fullPath,
            isDirectory,
            size,
            modifiedTime,
            fields[0],
            isSymbolicLink);
        return true;
    }

    private static DateTimeOffset? TryParseModifiedTime(string date, string time)
    {
        string value = $"{date} {time}";
        string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];
        return DateTimeOffset.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset result)
            ? result
            : null;
    }

    public static string NormalizePath(string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    public static string CombineAndroidPath(string directoryPath, string name) =>
        NormalizePath(directoryPath) == "/"
            ? "/" + name
            : NormalizePath(directoryPath) + "/" + name;
}
