using System.Collections.ObjectModel;
using System.IO;
using AndroidDevLink.Models;
using AndroidDevLink.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidDevLink.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAdbService _adbService;
    private readonly IScrcpyService _scrcpyService;
    private CancellationTokenSource? _deviceLoadCancellation;
    private CancellationTokenSource? _packageLoadCancellation;
    private CancellationTokenSource? _fileOperationCancellation;
    private CancellationTokenSource? _packageActionCancellation;
    private CancellationTokenSource? _deviceControlCancellation;
    private CancellationTokenSource? _autoRotateStateCancellation;
    private bool _disposed;
    private bool _isRefreshingDevices;

    [ObservableProperty]
    private AndroidDevice? selectedDevice;

    [ObservableProperty]
    private string? selectedSystemPackage;

    [ObservableProperty]
    private string? selectedThirdPartyPackage;

    [ObservableProperty]
    private string windowsPath = string.Empty;

    [ObservableProperty]
    private string androidPath = "/sdcard/";

    [ObservableProperty]
    private string systemPackageFilter = string.Empty;

    [ObservableProperty]
    private string thirdPartyPackageFilter = string.Empty;

    [ObservableProperty]
    private string statusMessage = "正在准备 ADB...";

    [ObservableProperty]
    private string systemPackagesEmptyMessage = "选择在线设备后自动读取系统包";

    [ObservableProperty]
    private string thirdPartyPackagesEmptyMessage = "选择在线设备后自动读取第三方包";

    [ObservableProperty]
    private bool isSystemPackagesEmptyMessageVisible = true;

    [ObservableProperty]
    private bool isThirdPartyPackagesEmptyMessageVisible = true;

    [ObservableProperty]
    private bool isFileOperationRunning;

    [ObservableProperty]
    private bool isPackageActionRunning;

    [ObservableProperty]
    private bool isDeviceControlRunning;

    [ObservableProperty]
    private bool? isAutoRotateEnabled;

    [ObservableProperty]
    private bool scrcpyNoAudio;

    public MainWindowViewModel(IAdbService adbService)
        : this(adbService, new ScrcpyService(), new AndroidFileService())
    {
    }

    public MainWindowViewModel(IAdbService adbService, IScrcpyService scrcpyService)
        : this(adbService, scrcpyService, new AndroidFileService())
    {
    }

    public MainWindowViewModel(
        IAdbService adbService,
        IScrcpyService scrcpyService,
        IAndroidFileService androidFileService)
    {
        _adbService = adbService ?? throw new ArgumentNullException(nameof(adbService));
        _scrcpyService = scrcpyService ?? throw new ArgumentNullException(nameof(scrcpyService));
        FileBrowser = new DeviceFileBrowserViewModel(
            androidFileService ?? throw new ArgumentNullException(nameof(androidFileService)), UploadQueue);

        RefreshDevicesCommand = new AsyncRelayCommand(
            RefreshDevicesAsync,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        RefreshPackagesCommand = new AsyncRelayCommand(
            RefreshPackagesAsync,
            CanRefreshPackages,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        InstallApkCommand = new AsyncRelayCommand(InstallApkAsync, CanRunFileOperation);
        PushFileCommand = new AsyncRelayCommand(PushFileAsync, () => SelectedDevice?.IsOnline == true);
        PullFileCommand = new AsyncRelayCommand(PullFileAsync, CanRunFileOperation);
        UninstallSystemPackageCommand = new AsyncRelayCommand(
            () => UninstallPackageAsync(SelectedSystemPackage),
            () => CanRunPackageAction(SelectedSystemPackage));
        LaunchSystemPackageCommand = new AsyncRelayCommand(
            () => LaunchPackageAsync(SelectedSystemPackage),
            () => CanRunPackageAction(SelectedSystemPackage));
        UninstallThirdPartyPackageCommand = new AsyncRelayCommand(
            () => UninstallPackageAsync(SelectedThirdPartyPackage),
            () => CanRunPackageAction(SelectedThirdPartyPackage));
        LaunchThirdPartyPackageCommand = new AsyncRelayCommand(
            () => LaunchPackageAsync(SelectedThirdPartyPackage),
            () => CanRunPackageAction(SelectedThirdPartyPackage));
        ScreenOffCommand = new AsyncRelayCommand(ScreenOffAsync, CanRunDeviceControl);
        ScreenOnCommand = new AsyncRelayCommand(ScreenOnAsync, CanRunDeviceControl);
        StartScrcpyCommand = new AsyncRelayCommand(StartScrcpyAsync, CanRunDeviceControl);
        ToggleAutoRotateCommand = new AsyncRelayCommand(ToggleAutoRotateAsync, CanRunDeviceControl);
        RotateScreen90Command = new AsyncRelayCommand(RotateScreen90Async, CanRunDeviceControl);
        CheckCozylaPackge = new AsyncRelayCommand(CheckCozylaPackageAsync);
    }

    public DeviceFileBrowserViewModel FileBrowser { get; }
    public UploadQueueViewModel UploadQueue { get; } = new();

    public ObservableCollection<AndroidDevice> Devices { get; } = [];
    public ObservableCollection<string> SystemPackages { get; } = [];
    public ObservableCollection<string> ThirdPartyPackages { get; } = [];
    public ObservableCollection<string> FilteredSystemPackages { get; } = [];
    public ObservableCollection<string> FilteredThirdPartyPackages { get; } = [];

    public string SelectedDeviceTitle => SelectedDevice is null
        ? "请选择一个设备"
        : $"当前设备：{SelectedDevice.Serial}";

    public IAsyncRelayCommand RefreshDevicesCommand { get; }
    public IAsyncRelayCommand RefreshPackagesCommand { get; }

    public IAsyncRelayCommand CheckCozylaPackge { get; }


    public IAsyncRelayCommand InstallApkCommand { get; }
    public IAsyncRelayCommand PushFileCommand { get; }
    public IAsyncRelayCommand PullFileCommand { get; }
    public IAsyncRelayCommand UninstallSystemPackageCommand { get; }
    public IAsyncRelayCommand LaunchSystemPackageCommand { get; }
    public IAsyncRelayCommand UninstallThirdPartyPackageCommand { get; }
    public IAsyncRelayCommand LaunchThirdPartyPackageCommand { get; }
    public IAsyncRelayCommand ScreenOffCommand { get; }
    public IAsyncRelayCommand ScreenOnCommand { get; }
    public IAsyncRelayCommand StartScrcpyCommand { get; }
    public IAsyncRelayCommand ToggleAutoRotateCommand { get; }
    public IAsyncRelayCommand RotateScreen90Command { get; }

    public string AutoRotateStatusText => IsAutoRotateEnabled switch
    {
        true => "自动旋转：已开启",
        false => "自动旋转：已关闭",
        null => "自动旋转：未知"
    };

    public string ToggleAutoRotateButtonText => IsAutoRotateEnabled switch
    {
        true => "关闭自动旋转",
        false => "开启自动旋转",
        null => "切换自动旋转"
    };

    public async Task RefreshDevicesAsync()
    {
        _deviceLoadCancellation?.Cancel();
        _deviceLoadCancellation?.Dispose();
        _deviceLoadCancellation = new CancellationTokenSource();

        try
        {
            SetStatus("正在读取已连接设备...");
            IReadOnlyList<AndroidDevice> devices = await _adbService.GetDevicesAsync(_deviceLoadCancellation.Token);

            string? previousSerial = SelectedDevice?.Serial;
            Devices.Clear();
            foreach (AndroidDevice device in devices)
            {
                Devices.Add(device);
            }

            _isRefreshingDevices = true;
            try
            {
                SelectedDevice = devices.FirstOrDefault(device => device.Serial == previousSerial) ?? devices.FirstOrDefault();
            }
            finally
            {
                _isRefreshingDevices = false;
            }

            if (SelectedDevice is null)
            {
                ClearPackages();
                SetPackageEmptyMessage("选择在线设备后自动读取包名");
                SetStatus("未检测到 Android 设备。");
            }
            else
            {
                await RefreshPackagesAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Devices.Clear();
            SelectedDevice = null;
            ClearPackages();
            SetStatus(exception.Message);
        }
    }

    public async Task RefreshPackagesAsync()
    {
        _packageLoadCancellation?.Cancel();
        _packageLoadCancellation?.Dispose();
        _packageLoadCancellation = new CancellationTokenSource();

        AndroidDevice? device = SelectedDevice;
        if (device is null)
        {
            ClearPackages();
            SetPackageEmptyMessage("选择在线设备后自动读取包名");
            return;
        }

        if (!device.IsOnline)
        {
            ClearPackages();
            SetPackageEmptyMessage($"设备状态为 {device.State}，无法读取包名");
            SetStatus($"设备 {device.Serial} 未处于 device 状态。");
            return;
        }

        try
        {
            ClearPackages();
            SetPackageEmptyMessage("正在读取包名...");
            SetStatus($"正在读取 {device.Serial} 的包列表...");

            Task<IReadOnlyList<string>> systemPackagesTask = _adbService.GetPackagesAsync(
                device.Serial,
                PackageKind.System,
                _packageLoadCancellation.Token);
            Task<IReadOnlyList<string>> thirdPartyPackagesTask = _adbService.GetPackagesAsync(
                device.Serial,
                PackageKind.ThirdParty,
                _packageLoadCancellation.Token);
            await Task.WhenAll(systemPackagesTask, thirdPartyPackagesTask);

            AddPackages(SystemPackages, await systemPackagesTask);
            AddPackages(ThirdPartyPackages, await thirdPartyPackagesTask);
            ApplyPackageFilters();
            SetStatus($"已读取系统包 {SystemPackages.Count} 个，第三方包 {ThirdPartyPackages.Count} 个。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ClearPackages();
            SetPackageEmptyMessage("读取包名失败");
            SetStatus(exception.Message);
        }
    }

    public void SetStatus(string message) => StatusMessage = message;

    public async Task<IReadOnlyList<string>> GetPackageApkPathsAsync(string packageName)
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return [];
        }

        try
        {
            SetStatus($"正在查询 {packageName} 的 APK 路径...");
            IReadOnlyList<string> paths = await _adbService.GetPackageApkPathsAsync(
                device.Serial,
                packageName,
                CancellationToken.None);
            SetStatus(paths.Count == 0
                ? $"未找到 {packageName} 的 APK 路径。"
                : $"已查询到 {paths.Count} 个 APK 路径。");
            return paths;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return [];
        }
    }

    public async Task<bool> ExtractApkToFileAsync(string packageName, string destinationDirectory)
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return false;
        }

        try
        {
            SetStatus($"正在查询 {packageName} 的 APK 路径...");
            IReadOnlyList<string> paths = await _adbService.GetPackageApkPathsAsync(
                device.Serial,
                packageName,
                CancellationToken.None);

            if (paths.Count == 0)
            {
                SetStatus($"未找到 {packageName} 的 APK 路径。");
                return false;
            }

            string packageDirectory = Path.Combine(destinationDirectory, packageName);
            Directory.CreateDirectory(packageDirectory);

            SetStatus($"正在提取 {packageName} 的 {paths.Count} 个 APK...");
            foreach (string sourcePath in paths)
            {
                string fileName = Path.GetFileName(sourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                await _adbService.PullAsync(
                    device.Serial,
                    sourcePath,
                    Path.Combine(packageDirectory, fileName),
                    CancellationToken.None);
            }

            SetStatus($"已提取 {packageName} 的 {paths.Count} 个 APK 到 {packageDirectory}。");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
    }

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        OnPropertyChanged(nameof(SelectedDeviceTitle));
        SelectedSystemPackage = null;
        SelectedThirdPartyPackage = null;
        NotifyCommandStates();
        _ = FileBrowser.SetDeviceAsync(value);
        _ = RefreshAutoRotateStateAsync(value);
        if (!_isRefreshingDevices)
        {
            _ = RefreshPackagesAsync();
        }
    }

    partial void OnSelectedSystemPackageChanged(string? value) => NotifyPackageCommandStates();

    partial void OnSelectedThirdPartyPackageChanged(string? value) => NotifyPackageCommandStates();

    partial void OnSystemPackageFilterChanged(string value) => ApplyPackageFilters();

    partial void OnThirdPartyPackageFilterChanged(string value) => ApplyPackageFilters();

    partial void OnIsFileOperationRunningChanged(bool value) => NotifyFileCommandStates();

    partial void OnIsPackageActionRunningChanged(bool value) => NotifyPackageCommandStates();

    partial void OnIsDeviceControlRunningChanged(bool value) => NotifyDeviceControlCommandStates();

    partial void OnIsAutoRotateEnabledChanged(bool? value)
    {
        OnPropertyChanged(nameof(AutoRotateStatusText));
        OnPropertyChanged(nameof(ToggleAutoRotateButtonText));
    }

    private bool CanRefreshPackages() => SelectedDevice?.IsOnline == true;

    private bool CanRunFileOperation() => SelectedDevice?.IsOnline == true && !IsFileOperationRunning;

    private bool CanRunPackageAction(string? packageName) =>
        SelectedDevice?.IsOnline == true && !IsPackageActionRunning && !string.IsNullOrWhiteSpace(packageName);

    private bool CanRunDeviceControl() => SelectedDevice?.IsOnline == true && !IsDeviceControlRunning;

    private async Task InstallApkAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null || !TryGetWindowsSourceFile(out string apkPath))
        {
            return;
        }

        if (!string.Equals(Path.GetExtension(apkPath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("安装操作仅支持 .apk 文件。");
            return;
        }

        await RunFileOperationAsync(
            $"正在向 {device.Serial} 安装 {Path.GetFileName(apkPath)}...",
            $"APK 已安装：{Path.GetFileName(apkPath)}",
            cancellationToken => _adbService.InstallApkAsync(device.Serial, apkPath, cancellationToken));
    }

    private Task PushFileAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        string localPath = WindowsPath.Trim();
        if (device is null || !TryGetAndroidPath(out string remotePath))
        {
            return Task.CompletedTask;
        }
        if (string.IsNullOrWhiteSpace(localPath))
        {
            SetStatus("请输入 Windows 源文件路径。");
            return Task.CompletedTask;
        }

        try
        {
            localPath = Path.GetFullPath(localPath);
            UploadQueue.Enqueue(device.Serial, localPath, remotePath,
                token => _adbService.PushAsync(device.Serial, localPath, remotePath, token));
            SetStatus($"已加入上传队列：{Path.GetFileName(localPath)} → {remotePath}");
        }
        catch (Exception exception) { SetStatus(exception.Message); }
        return Task.CompletedTask;
    }

    private async Task PullFileAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null || !TryGetAndroidPath(out string remotePath) || !TryGetWindowsDestination(out string localPath))
        {
            return;
        }

        await RunFileOperationAsync(
            $"正在从 {remotePath} 拉取文件...",
            $"文件已拉取到 {localPath}",
            cancellationToken => _adbService.PullAsync(device.Serial, remotePath, localPath, cancellationToken));
    }

    private async Task UninstallPackageAsync(string? packageName)
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null || string.IsNullOrWhiteSpace(packageName))
        {
            SetStatus("请先选择一个包名。");
            return;
        }

        bool uninstalled = await RunPackageActionAsync(
            $"正在卸载 {packageName}...",
            $"已卸载：{packageName}",
            cancellationToken => _adbService.UninstallAsync(device.Serial, packageName, cancellationToken));

        if (uninstalled)
        {
            await RefreshPackagesAsync();
        }
    }

    private async Task LaunchPackageAsync(string? packageName)
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null || string.IsNullOrWhiteSpace(packageName))
        {
            SetStatus("请先选择一个包名。");
            return;
        }

        await RunPackageActionAsync(
            $"正在启动 {packageName}...",
            $"已请求启动：{packageName}",
            cancellationToken => _adbService.LaunchLauncherAsync(device.Serial, packageName, cancellationToken));
    }

    private async Task ScreenOffAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return;
        }

        await RunDeviceControlAsync(
            $"正在令设备 {device.Serial} 息屏...",
            "已发送息屏指令。",
            cancellationToken => _adbService.ScreenOffAsync(device.Serial, cancellationToken));
    }

    private async Task ScreenOnAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return;
        }

        await RunDeviceControlAsync(
            $"正在唤醒设备 {device.Serial}...",
            "已发送亮屏指令。",
            cancellationToken => _adbService.ScreenOnAsync(device.Serial, cancellationToken));
    }

    private async Task StartScrcpyAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return;
        }

        await RunDeviceControlAsync(
            $"正在启动设备 {device.Serial} 的 scrcpy...",
            $"已启动设备 {device.Serial} 的镜像控制。",
            cancellationToken => _scrcpyService.StartAsync(device.Serial, ScrcpyNoAudio, cancellationToken));
    }

    private async Task ToggleAutoRotateAsync()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return;
        }

        await RunDeviceControlAsync(
            $"正在切换设备 {device.Serial} 的自动旋转...",
            async cancellationToken =>
            {
                bool currentEnabled = await _adbService.GetAutoRotateEnabledAsync(device.Serial, cancellationToken);
                bool newEnabled = !currentEnabled;
                await _adbService.SetAutoRotateEnabledAsync(device.Serial, newEnabled, cancellationToken);
                IsAutoRotateEnabled = newEnabled;
                return newEnabled ? "已开启自动旋转。" : "已关闭自动旋转。";
            });
    }

    private async Task RotateScreen90Async()
    {
        AndroidDevice? device = GetSelectedOnlineDevice();
        if (device is null)
        {
            return;
        }

        await RunDeviceControlAsync(
            $"正在旋转设备 {device.Serial} 的屏幕...",
            async cancellationToken =>
            {
                // 自动旋转开启时先关闭重力感应，避免刚设置的方向被系统立即纠正回去。
                bool autoRotateEnabled = await _adbService.GetAutoRotateEnabledAsync(device.Serial, cancellationToken);
                if (autoRotateEnabled)
                {
                    await _adbService.SetAutoRotateEnabledAsync(device.Serial, false, cancellationToken);
                    IsAutoRotateEnabled = false;
                }

                int currentRotation = await _adbService.GetUserRotationAsync(device.Serial, cancellationToken);
                int nextRotation = (currentRotation + 1) % 4;
                await _adbService.SetUserRotationAsync(device.Serial, nextRotation, cancellationToken);

                return $"已将屏幕旋转到 {nextRotation * 90} 度。";
            });
    }

    private async Task RefreshAutoRotateStateAsync(AndroidDevice? device)
    {
        _autoRotateStateCancellation?.Cancel();
        _autoRotateStateCancellation?.Dispose();
        _autoRotateStateCancellation = new CancellationTokenSource();

        if (device?.IsOnline != true)
        {
            IsAutoRotateEnabled = null;
            return;
        }

        try
        {
            IsAutoRotateEnabled = await _adbService.GetAutoRotateEnabledAsync(
                device.Serial,
                _autoRotateStateCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            IsAutoRotateEnabled = null;
        }
    }

    private Task CheckCozylaPackageAsync()
    {
        const string cozylaFilter = "cozyla";
        SystemPackageFilter = cozylaFilter;
        ThirdPartyPackageFilter = cozylaFilter;
        SetStatus("已按 cozyla 过滤包名。");
        return Task.CompletedTask;
    }

    private AndroidDevice? GetSelectedOnlineDevice()
    {
        if (SelectedDevice is null)
        {
            SetStatus("请先选择一台设备。");
            return null;
        }

        if (!SelectedDevice.IsOnline)
        {
            SetStatus($"设备 {SelectedDevice.Serial} 未处于 device 状态。");
            return null;
        }

        return SelectedDevice;
    }

    private bool TryGetWindowsSourceFile(out string localPath)
    {
        localPath = WindowsPath.Trim();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            SetStatus("请输入 Windows 源文件路径。");
            return false;
        }

        if (!File.Exists(localPath))
        {
            SetStatus("Windows 源文件不存在。");
            return false;
        }

        return true;
    }

    private bool TryGetWindowsDestination(out string localPath)
    {
        localPath = WindowsPath.Trim();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            SetStatus("请输入 Windows 目标路径。");
            return false;
        }

        if (Directory.Exists(localPath))
        {
            return true;
        }

        string? destinationDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory) && !Directory.Exists(destinationDirectory))
        {
            SetStatus("Windows 目标目录不存在。");
            return false;
        }

        return true;
    }

    private bool TryGetAndroidPath(out string remotePath)
    {
        remotePath = AndroidPath.Trim();
        if (!string.IsNullOrWhiteSpace(remotePath))
        {
            return true;
        }

        SetStatus("请输入 Android 设备路径。");
        return false;
    }

    private async Task RunFileOperationAsync(
        string startingMessage,
        string completedMessage,
        Func<CancellationToken, Task> operation)
    {
        _fileOperationCancellation?.Cancel();
        _fileOperationCancellation?.Dispose();
        _fileOperationCancellation = new CancellationTokenSource();
        IsFileOperationRunning = true;

        try
        {
            SetStatus(startingMessage);
            await operation(_fileOperationCancellation.Token);
            SetStatus(completedMessage);
        }
        catch (OperationCanceledException)
        {
            SetStatus("文件操作已取消。");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsFileOperationRunning = false;
        }
    }

    private async Task<bool> RunPackageActionAsync(
        string startingMessage,
        string completedMessage,
        Func<CancellationToken, Task> operation)
    {
        _packageActionCancellation?.Cancel();
        _packageActionCancellation?.Dispose();
        _packageActionCancellation = new CancellationTokenSource();
        IsPackageActionRunning = true;

        try
        {
            SetStatus(startingMessage);
            await operation(_packageActionCancellation.Token);
            SetStatus(completedMessage);
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus("包管理操作已取消。");
            return false;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
            return false;
        }
        finally
        {
            IsPackageActionRunning = false;
        }
    }

    private Task RunDeviceControlAsync(
        string startingMessage,
        string completedMessage,
        Func<CancellationToken, Task> operation) =>
        RunDeviceControlAsync(startingMessage, async cancellationToken =>
        {
            await operation(cancellationToken);
            return completedMessage;
        });

    private async Task RunDeviceControlAsync(
        string startingMessage,
        Func<CancellationToken, Task<string>> operation)
    {
        _deviceControlCancellation?.Cancel();
        _deviceControlCancellation?.Dispose();
        _deviceControlCancellation = new CancellationTokenSource();
        IsDeviceControlRunning = true;

        try
        {
            SetStatus(startingMessage);
            string completedMessage = await operation(_deviceControlCancellation.Token);
            SetStatus(completedMessage);
        }
        catch (OperationCanceledException)
        {
            SetStatus("设备控制操作已取消。");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            IsDeviceControlRunning = false;
        }
    }

    private void ClearPackages()
    {
        SystemPackages.Clear();
        ThirdPartyPackages.Clear();
        FilteredSystemPackages.Clear();
        FilteredThirdPartyPackages.Clear();
        SelectedSystemPackage = null;
        SelectedThirdPartyPackage = null;
    }

    private void ApplyPackageFilters()
    {
        ApplyPackageFilter(SystemPackages, FilteredSystemPackages, SystemPackageFilter);
        ApplyPackageFilter(ThirdPartyPackages, FilteredThirdPartyPackages, ThirdPartyPackageFilter);
        UpdatePackageEmptyStates();
    }

    private static void ApplyPackageFilter(
        IEnumerable<string> source,
        ObservableCollection<string> target,
        string? filter)
    {
        target.Clear();
        foreach (string packageName in source.Where(packageName =>
                     string.IsNullOrWhiteSpace(filter) ||
                     packageName.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(packageName);
        }
    }

    private void UpdatePackageEmptyStates()
    {
        UpdatePackageEmptyState(
            SystemPackages,
            FilteredSystemPackages,
            "没有可显示的包名",
            "没有匹配的包名",
            message => SystemPackagesEmptyMessage = message,
            visible => IsSystemPackagesEmptyMessageVisible = visible);
        UpdatePackageEmptyState(
            ThirdPartyPackages,
            FilteredThirdPartyPackages,
            "没有可显示的包名",
            "没有匹配的包名",
            message => ThirdPartyPackagesEmptyMessage = message,
            visible => IsThirdPartyPackagesEmptyMessageVisible = visible);
    }

    private static void UpdatePackageEmptyState(
        ObservableCollection<string> allPackages,
        ObservableCollection<string> filteredPackages,
        string noPackagesMessage,
        string noMatchesMessage,
        Action<string> setMessage,
        Action<bool> setVisibility)
    {
        if (allPackages.Count == 0)
        {
            setMessage(noPackagesMessage);
            setVisibility(true);
            return;
        }

        bool hasMatches = filteredPackages.Count > 0;
        setMessage(hasMatches ? string.Empty : noMatchesMessage);
        setVisibility(!hasMatches);
    }

    private static void AddPackages(ObservableCollection<string> target, IReadOnlyList<string> packages)
    {
        foreach (string packageName in packages)
        {
            target.Add(packageName);
        }
    }

    private void SetPackageEmptyMessage(string message)
    {
        SystemPackagesEmptyMessage = message;
        ThirdPartyPackagesEmptyMessage = message;
        IsSystemPackagesEmptyMessageVisible = SystemPackages.Count == 0;
        IsThirdPartyPackagesEmptyMessageVisible = ThirdPartyPackages.Count == 0;
    }

    private void NotifyCommandStates()
    {
        RefreshPackagesCommand.NotifyCanExecuteChanged();
        NotifyFileCommandStates();
        NotifyPackageCommandStates();
        NotifyDeviceControlCommandStates();
    }

    private void NotifyFileCommandStates()
    {
        InstallApkCommand.NotifyCanExecuteChanged();
        PushFileCommand.NotifyCanExecuteChanged();
        PullFileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPackageCommandStates()
    {
        UninstallSystemPackageCommand.NotifyCanExecuteChanged();
        LaunchSystemPackageCommand.NotifyCanExecuteChanged();
        UninstallThirdPartyPackageCommand.NotifyCanExecuteChanged();
        LaunchThirdPartyPackageCommand.NotifyCanExecuteChanged();
    }

    private void NotifyDeviceControlCommandStates()
    {
        ScreenOffCommand.NotifyCanExecuteChanged();
        ScreenOnCommand.NotifyCanExecuteChanged();
        StartScrcpyCommand.NotifyCanExecuteChanged();
        ToggleAutoRotateCommand.NotifyCanExecuteChanged();
        RotateScreen90Command.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        FileBrowser.Dispose();
        UploadQueue.Dispose();
        CancelAndDispose(ref _deviceLoadCancellation);
        CancelAndDispose(ref _packageLoadCancellation);
        CancelAndDispose(ref _fileOperationCancellation);
        CancelAndDispose(ref _packageActionCancellation);
        CancelAndDispose(ref _deviceControlCancellation);
        CancelAndDispose(ref _autoRotateStateCancellation);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
    }
}
