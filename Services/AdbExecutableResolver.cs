using System.IO;

namespace AndroidDevLink.Services;

public static class AdbExecutableResolver
{
    public static string Resolve()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] sdkRoots =
        [
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? string.Empty,
            Environment.GetEnvironmentVariable("ANDROID_HOME") ?? string.Empty,
            Path.Combine(localAppData, "Android", "Sdk")
        ];

        foreach (string sdkRoot in sdkRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string adbPath = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
            if (File.Exists(adbPath))
            {
                return adbPath;
            }
        }

        return "adb.exe";
    }
}
