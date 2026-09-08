using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public interface IAdbService
{
    Task<IReadOnlyList<AndroidDevice>> GetDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPackagesAsync(
        string serial,
        PackageKind packageKind,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetPackageApkPathsAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken);

    Task InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken);

    Task PushAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken);

    Task PullAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken);

    Task UninstallAsync(string serial, string packageName, CancellationToken cancellationToken);

    Task LaunchLauncherAsync(string serial, string packageName, CancellationToken cancellationToken);

    Task ScreenOffAsync(string serial, CancellationToken cancellationToken);

    Task ScreenOnAsync(string serial, CancellationToken cancellationToken);

    Task<bool> GetAutoRotateEnabledAsync(string serial, CancellationToken cancellationToken);

    Task SetAutoRotateEnabledAsync(string serial, bool enabled, CancellationToken cancellationToken);

    Task<int> GetUserRotationAsync(string serial, CancellationToken cancellationToken);

    Task SetUserRotationAsync(string serial, int rotation, CancellationToken cancellationToken);
}
