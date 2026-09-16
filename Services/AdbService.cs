using System.ComponentModel;
using System.IO;
using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public sealed class AdbService : IAdbService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _adbExecutable;

    public AdbService()
        : this(new ProcessRunner(), ResolveAdbExecutable())
    {
    }

    public AdbService(IProcessRunner processRunner, string adbExecutable)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _adbExecutable = string.IsNullOrWhiteSpace(adbExecutable)
            ? throw new ArgumentException("ADB 可执行文件路径不能为空。", nameof(adbExecutable))
            : adbExecutable;
    }

    public AdbDeviceMonitor StartDeviceMonitoring(Action onChanged) =>
        new(_adbExecutable, onChanged ?? throw new ArgumentNullException(nameof(onChanged)));

    public async Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(["devices", "-l"], cancellationToken);
        EnsureSuccessful(result);
        return AdbOutputParser.ParseDevices(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> GetPackagesAsync(
        string serial,
        PackageKind packageKind,
        CancellationToken cancellationToken)
    {
        string flag = packageKind == PackageKind.System ? "-s" : "-3";
        ProcessResult result = await RunAsync(
            ["-s", serial, "shell", "pm", "list", "packages", flag],
            cancellationToken);
        EnsureSuccessful(result);
        return AdbOutputParser.ParsePackages(result.StandardOutput);
    }

    public async Task<IReadOnlyList<string>> GetPackageApkPathsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            ["-s", serial, "shell", "pm", "path", packageName],
            cancellationToken);
        EnsureSuccessful(result);
        return AdbOutputParser.ParsePackageApkPaths(result.StandardOutput);
    }

    public Task InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "install", "-r", apkPath], cancellationToken);

    public Task PushAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "push", localPath, remotePath], cancellationToken);

    public Task PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "pull", remotePath, localPath], cancellationToken);

    public Task UninstallAsync(string serial, string packageName, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "uninstall", packageName], cancellationToken);

    // monkey 会通过系统的 Intent 解析器找到包对应的 LAUNCHER Activity。
    public Task LaunchLauncherAsync(string serial, string packageName, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(
            ["-s", serial, "shell", "monkey", "-p", packageName, "-c", "android.intent.category.LAUNCHER", "1"],
            cancellationToken);

    // 使用明确的休眠/唤醒按键，避免 KEYCODE_POWER 的切换语义造成反向操作。
    public Task ScreenOffAsync(string serial, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "shell", "input", "keyevent", "KEYCODE_SLEEP"], cancellationToken);

    public Task ScreenOnAsync(string serial, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(["-s", serial, "shell", "input", "keyevent", "KEYCODE_WAKEUP"], cancellationToken);

    public async Task<bool> GetAutoRotateEnabledAsync(string serial, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            ["-s", serial, "shell", "settings", "get", "system", "accelerometer_rotation"],
            cancellationToken);
        EnsureSuccessful(result);
        return AdbOutputParser.ParseSettingsBoolean(result.StandardOutput);
    }

    public Task SetAutoRotateEnabledAsync(string serial, bool enabled, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(
            ["-s", serial, "shell", "settings", "put", "system", "accelerometer_rotation", enabled ? "1" : "0"],
            cancellationToken);

    public async Task<int> GetUserRotationAsync(string serial, CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(
            ["-s", serial, "shell", "settings", "get", "system", "user_rotation"],
            cancellationToken);
        EnsureSuccessful(result);
        return AdbOutputParser.ParseSettingsRotation(result.StandardOutput);
    }

    // rotation: 0=0°、1=90°、2=180°、3=270°，对应 Surface.ROTATION_* 常量。
    public Task SetUserRotationAsync(string serial, int rotation, CancellationToken cancellationToken) =>
        RunSuccessfulAsync(
            ["-s", serial, "shell", "settings", "put", "system", "user_rotation", rotation.ToString()],
            cancellationToken);

    private async Task RunSuccessfulAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessResult result = await RunAsync(arguments, cancellationToken);
        EnsureSuccessful(result);
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(_adbExecutable, arguments, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "未找到 adb.exe。请安装 Android SDK Platform-Tools，并将其目录加入 PATH。",
                exception);
        }
    }

    private static void EnsureSuccessful(ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw CreateAdbException(result);
        }
    }

    private static InvalidOperationException CreateAdbException(ProcessResult result)
    {
        string details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        return new InvalidOperationException($"ADB 命令执行失败：{details.Trim()}");
    }

    private static string ResolveAdbExecutable()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] sdkRoots =
        [
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? string.Empty,
            Environment.GetEnvironmentVariable("ANDROID_HOME") ?? string.Empty,
            Path.Combine(localAppData, "Android", "Sdk")
        ];

        // 依次尝试随应用发布的 ADB、环境变量和 Android SDK 默认目录。
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
