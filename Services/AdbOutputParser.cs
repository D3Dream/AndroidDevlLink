using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public static class AdbOutputParser
{
    public static IReadOnlyList<AndroidDevice> ParseDevices(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        List<AndroidDevice> devices = [];
        foreach (string rawLine in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.StartsWith("List of devices attached", StringComparison.Ordinal) || rawLine.StartsWith('*'))
            {
                continue;
            }

            string[] fields = rawLine.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            string description = string.Join(" ", fields.Skip(2)
                .Where(field => field.StartsWith("model:", StringComparison.Ordinal) ||
                                field.StartsWith("product:", StringComparison.Ordinal)));
            devices.Add(new AndroidDevice(fields[0], fields[1], description));
        }

        return devices;
    }

    public static IReadOnlyList<string> ParsePackages(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return ParsePackagePrefixedLines(output)
            .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ParsePackageApkPaths(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ParsePackagePrefixedLines(output);
    }

    public static bool ParseSettingsBoolean(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Trim() == "1";
    }

    // "settings get" 在键不存在时输出 "null"；此时视为默认竖屏（0°）。
    public static int ParseSettingsRotation(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string trimmed = output.Trim();
        return int.TryParse(trimmed, out int rotation) && rotation is >= 0 and <= 3 ? rotation : 0;
    }

    private static string[] ParsePackagePrefixedLines(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
        .Select(line => line["package:".Length..])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();
}
