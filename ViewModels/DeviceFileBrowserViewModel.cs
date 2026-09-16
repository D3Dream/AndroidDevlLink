using System.Collections.ObjectModel;
using System.IO;
using AndroidDevLink.Models;
using AndroidDevLink.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidDevLink.ViewModels;

public sealed partial class DeviceFileBrowserViewModel : ObservableObject, IDisposable
{
    private const string DefaultPath = "/sdcard";
    private readonly IAndroidFileService _fileService;
    private readonly IAppLogService _logService;
    private readonly Stack<string> _backHistory = [];
    private readonly Stack<string> _forwardHistory = [];
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _downloadCancellation;
    private CancellationTokenSource? _createDirectoryCancellation;
    private CancellationTokenSource? _entryOperationCancellation;
    private CancellationTokenSource? _mediaScanCancellation;
    private bool _disposed;

    [ObservableProperty]
    private AndroidDevice? selectedDevice;

    [ObservableProperty]
    private AndroidFileEntry? selectedEntry;

    public ObservableCollection<AndroidFileEntry> SelectedEntries { get; } = [];

    [ObservableProperty]
    private string currentPath = DefaultPath;

    [ObservableProperty]
    private string statusMessage = "等待选择在线设备";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string searchText = string.Empty;

    public DeviceFileBrowserViewModel(IAndroidFileService fileService)
        : this(fileService, new UploadQueueViewModel(), new AppLogService())
    {
        _ownsUploadQueue = true;
    }

    private readonly bool _ownsUploadQueue;
    public UploadQueueViewModel UploadQueue { get; }

    public DeviceFileBrowserViewModel(IAndroidFileService fileService, UploadQueueViewModel uploadQueue)
        : this(fileService, uploadQueue, new AppLogService())
    {
    }

    public DeviceFileBrowserViewModel(
        IAndroidFileService fileService,
        UploadQueueViewModel uploadQueue,
        IAppLogService logService)
    {
        UploadQueue = uploadQueue ?? throw new ArgumentNullException(nameof(uploadQueue));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        UploadQueue.UploadCompleted += OnUploadCompleted;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanLoad);
        GoBackCommand = new AsyncRelayCommand(GoBackAsync, () => CanLoad() && _backHistory.Count > 0);
        GoForwardCommand = new AsyncRelayCommand(GoForwardAsync, () => CanLoad() && _forwardHistory.Count > 0);
        GoUpCommand = new AsyncRelayCommand(GoUpAsync, () => CanLoad() && CurrentPath != "/");
        OpenSelectedEntryCommand = new AsyncRelayCommand(OpenSelectedEntryAsync, CanOpenSelectedEntry);
        DownloadSelectedFileCommand = new AsyncRelayCommand<string>(DownloadSelectedFileAsync, CanDownloadSelectedFile);
        UploadFileCommand = new AsyncRelayCommand<string>(UploadFileAsync, CanUploadFile);
        CreateDirectoryCommand = new AsyncRelayCommand<string>(CreateDirectoryAsync, CanCreateDirectory);
        DownloadSelectedEntryCommand = new AsyncRelayCommand<string>(DownloadSelectedEntryAsync, CanDownloadSelectedEntry);
        RenameSelectedEntryCommand = new AsyncRelayCommand<string>(RenameSelectedEntryAsync, CanRenameSelectedEntry);
        DeleteSelectedEntryCommand = new AsyncRelayCommand(DeleteSelectedEntryAsync, CanModifySelectedEntry);
        DeleteSelectedEntriesCommand = new AsyncRelayCommand(DeleteSelectedEntriesAsync, CanDeleteSelectedEntries);
        ScanSelectedMediaCommand = new AsyncRelayCommand(ScanSelectedMediaAsync, CanScanSelectedMedia);
        ScanMediaDirectoryCommand = new AsyncRelayCommand(ScanMediaDirectoryAsync, CanLoad);
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _logService.Add(value);
        }
    }

    public ObservableCollection<AndroidFileEntry> Entries { get; } = [];
    public ObservableCollection<AndroidFileEntry> VisibleEntries { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }
    public IAsyncRelayCommand GoForwardCommand { get; }
    public IAsyncRelayCommand GoUpCommand { get; }
    public IAsyncRelayCommand OpenSelectedEntryCommand { get; }
    public IAsyncRelayCommand<string> DownloadSelectedFileCommand { get; }
    public IAsyncRelayCommand<string> UploadFileCommand { get; }
    public IAsyncRelayCommand<string> CreateDirectoryCommand { get; }
    public IAsyncRelayCommand<string> DownloadSelectedEntryCommand { get; }
    public IAsyncRelayCommand<string> RenameSelectedEntryCommand { get; }
    public IAsyncRelayCommand DeleteSelectedEntryCommand { get; }
    public IAsyncRelayCommand DeleteSelectedEntriesCommand { get; }
    public IAsyncRelayCommand ScanSelectedMediaCommand { get; }
    public IAsyncRelayCommand ScanMediaDirectoryCommand { get; }

    public void SetSelectedEntries(IEnumerable<AndroidFileEntry> entries)
    {
        SelectedEntries.Clear();
        foreach (AndroidFileEntry entry in entries.Where(entry => !entry.IsDirectory))
        {
            SelectedEntries.Add(entry);
        }

        SelectedEntry = SelectedEntries.LastOrDefault();
        DeleteSelectedEntriesCommand.NotifyCanExecuteChanged();
    }

    public string BreadcrumbText => SelectedDevice is null
        ? "未选择设备"
        : $"{SelectedDevice.Serial}  ›  {CurrentPath.TrimStart('/').Replace("/", "  ›  ")}";

    public string ItemCountText => IsLoading
        ? "正在加载..."
        : string.IsNullOrWhiteSpace(SearchText)
            ? $"共 {Entries.Count} 个项目"
            : $"显示 {VisibleEntries.Count} / {Entries.Count} 个项目";

    public async Task SetDeviceAsync(AndroidDevice? device)
    {
        if (Equals(SelectedDevice, device))
        {
            return;
        }

        SelectedDevice = device;
        _backHistory.Clear();
        _forwardHistory.Clear();
        CurrentPath = DefaultPath;
        SearchText = string.Empty;
        Entries.Clear();
        VisibleEntries.Clear();
        NotifyNavigationStates();

        if (device is null)
        {
            StatusMessage = "等待选择在线设备";
            return;
        }

        if (!device.IsOnline)
        {
            StatusMessage = $"设备 {device.Serial} 未处于 device 状态";
            return;
        }

        await LoadDirectoryAsync(CurrentPath);
    }

    public Task RefreshAsync() => CanLoad() ? LoadDirectoryAsync(CurrentPath) : Task.CompletedTask;

    public async Task NavigateToAsync(string path, bool addToHistory = true)
    {
        if (!CanLoad())
        {
            return;
        }

        string normalizedPath = AndroidFileOutputParser.NormalizePath(path);
        if (normalizedPath == CurrentPath)
        {
            await RefreshAsync();
            return;
        }

        string previousPath = CurrentPath;
        bool loaded = await LoadDirectoryAsync(normalizedPath);
        if (!loaded)
        {
            return;
        }

        if (addToHistory)
        {
            _backHistory.Push(previousPath);
            _forwardHistory.Clear();
        }

        NotifyNavigationStates();
    }

    private async Task<bool> LoadDirectoryAsync(string path)
    {
        AndroidDevice? device = SelectedDevice;
        if (device?.IsOnline != true)
        {
            return false;
        }

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        IsLoading = true;

        try
        {
            string normalizedPath = AndroidFileOutputParser.NormalizePath(path);
            StatusMessage = $"正在读取 {normalizedPath}...";
            IReadOnlyList<AndroidFileEntry> entries = await _fileService.ListDirectoryAsync(
                device.Serial,
                normalizedPath,
                _loadCancellation.Token);

            Entries.Clear();
            foreach (AndroidFileEntry entry in entries)
            {
                Entries.Add(entry);
            }
            ApplySearchFilter();

            CurrentPath = normalizedPath;
            StatusMessage = $"已读取 {normalizedPath}";
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenSelectedEntryAsync()
    {
        if (SelectedEntry?.IsDirectory == true)
        {
            await NavigateToAsync(SelectedEntry.FullPath);
        }
    }

    private Task DownloadSelectedFileAsync(string? localFilePath) =>
        DownloadSelectedEntryAsync(localFilePath);

    private async Task DownloadSelectedEntryAsync(string? localTargetPath)
    {
        AndroidDevice? device = SelectedDevice;
        AndroidFileEntry? entry = SelectedEntry;
        if (device?.IsOnline != true || entry is null || string.IsNullOrWhiteSpace(localTargetPath))
        {
            return;
        }

        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();

        try
        {
            StatusMessage = $"正在下载 {entry.Name}...";
            await _fileService.PullFileAsync(
                device.Serial,
                entry.FullPath,
                localTargetPath,
                _downloadCancellation.Token);
            StatusMessage = $"已下载到 {localTargetPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "下载已取消";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private Task UploadFileAsync(string? localFilePath)
    {
        AndroidDevice? device = SelectedDevice;
        if (device?.IsOnline != true || string.IsNullOrWhiteSpace(localFilePath))
        {
            return Task.CompletedTask;
        }

        try
        {
            localFilePath = Path.GetFullPath(localFilePath);
            string fileName = Path.GetFileName(localFilePath);
            string destination = CurrentPath;
            UploadQueue.Enqueue(device.Serial, localFilePath, destination,
                token => _fileService.PushFileAsync(device.Serial, localFilePath, destination, token),
                async token =>
                {
                    if (IsMediaFileName(fileName))
                        await _fileService.ScanMediaFileAsync(device.Serial,
                            AndroidFileOutputParser.CombineAndroidPath(destination, fileName), token);
                });
            StatusMessage = $"已加入上传队列：{fileName} → {destination}（完成后可刷新目录）";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        return Task.CompletedTask;
    }

    private async Task CreateDirectoryAsync(string? directoryName)
    {
        AndroidDevice? device = SelectedDevice;
        if (device?.IsOnline != true || string.IsNullOrWhiteSpace(directoryName))
        {
            return;
        }

        _createDirectoryCancellation?.Cancel();
        _createDirectoryCancellation?.Dispose();
        _createDirectoryCancellation = new CancellationTokenSource();

        try
        {
            string validName = AndroidFileService.ValidateDirectoryName(directoryName);
            StatusMessage = $"正在创建文件夹 {validName}...";
            await _fileService.CreateDirectoryAsync(
                device.Serial,
                CurrentPath,
                validName,
                _createDirectoryCancellation.Token);
            await LoadDirectoryAsync(CurrentPath);
            StatusMessage = $"已创建文件夹 {validName}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "新建文件夹已取消";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task RenameSelectedEntryAsync(string? newName)
    {
        AndroidDevice? device = SelectedDevice;
        AndroidFileEntry? entry = SelectedEntry;
        if (device?.IsOnline != true || entry is null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        _entryOperationCancellation?.Cancel();
        _entryOperationCancellation?.Dispose();
        _entryOperationCancellation = new CancellationTokenSource();

        try
        {
            string validName = AndroidFileService.ValidateEntryName(newName);
            if (string.Equals(entry.Name, validName, StringComparison.Ordinal))
            {
                StatusMessage = "名称未发生变化";
                return;
            }

            StatusMessage = $"正在将 {entry.Name} 重命名为 {validName}...";
            await _fileService.RenameEntryAsync(
                device.Serial,
                entry.FullPath,
                validName,
                _entryOperationCancellation.Token);
            await LoadDirectoryAsync(CurrentPath);
            StatusMessage = $"已将 {entry.Name} 重命名为 {validName}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "重命名已取消";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task DeleteSelectedEntryAsync()
    {
        AndroidDevice? device = SelectedDevice;
        AndroidFileEntry? entry = SelectedEntry;
        if (device?.IsOnline != true || entry is null)
        {
            return;
        }

        _entryOperationCancellation?.Cancel();
        _entryOperationCancellation?.Dispose();
        _entryOperationCancellation = new CancellationTokenSource();

        try
        {
            StatusMessage = $"正在删除 {entry.FullPath}...";
            await _fileService.DeleteEntryAsync(
                device.Serial,
                entry.FullPath,
                entry.IsDirectory,
                _entryOperationCancellation.Token);
            await LoadDirectoryAsync(CurrentPath);
            StatusMessage = $"已删除 {entry.FullPath}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "删除已取消";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task DeleteSelectedEntriesAsync()
    {
        AndroidDevice? device = SelectedDevice;
        AndroidFileEntry[] entries = SelectedEntries.Where(entry => !entry.IsDirectory).ToArray();
        if (device?.IsOnline != true || entries.Length == 0)
        {
            return;
        }

        _entryOperationCancellation?.Cancel();
        _entryOperationCancellation?.Dispose();
        _entryOperationCancellation = new CancellationTokenSource();

        int deleted = 0;
        List<string> failures = [];
        try
        {
            foreach (AndroidFileEntry entry in entries)
            {
                try
                {
                    await _fileService.DeleteEntryAsync(
                        device.Serial, entry.FullPath, false, _entryOperationCancellation.Token);
                    deleted++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add($"{entry.Name}: {exception.Message}");
                }
            }

            await LoadDirectoryAsync(CurrentPath);
            StatusMessage = failures.Count == 0
                ? $"已删除 {deleted} 个文件。"
                : $"已删除 {deleted} 个文件，失败 {failures.Count} 个：{string.Join("；", failures)}";
            SelectedEntries.Clear();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"删除已取消，已删除 {deleted} 个文件。";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task ScanSelectedMediaAsync()
    {
        AndroidDevice? device = SelectedDevice;
        AndroidFileEntry? entry = SelectedEntry;
        if (device?.IsOnline != true || entry?.IsMediaFile != true)
        {
            return;
        }

        await RunMediaScanAsync(
            $"正在扫描媒体文件 {entry.Name}...",
            $"已请求扫描媒体文件：{entry.FullPath}",
            cancellationToken => _fileService.ScanMediaFileAsync(device.Serial, entry.FullPath, cancellationToken));
    }

    private async Task ScanMediaDirectoryAsync()
    {
        AndroidDevice? device = SelectedDevice;
        if (device?.IsOnline != true)
        {
            return;
        }

        AndroidFileEntry[] mediaEntries = Entries.Where(entry => entry.IsMediaFile).ToArray();
        if (mediaEntries.Length == 0)
        {
            StatusMessage = "当前目录没有可扫描的媒体文件。";
            return;
        }

        await RunMediaScanAsync(
            $"正在扫描当前目录的 {mediaEntries.Length} 个媒体文件...",
            $"已请求扫描当前目录的 {mediaEntries.Length} 个媒体文件。",
            async cancellationToken =>
            {
                for (int index = 0; index < mediaEntries.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AndroidFileEntry entry = mediaEntries[index];
                    StatusMessage = $"正在扫描媒体文件 {index + 1}/{mediaEntries.Length}：{entry.Name}";
                    await _fileService.ScanMediaFileAsync(device.Serial, entry.FullPath, cancellationToken);
                }
            });
    }

    private async Task RunMediaScanAsync(
        string startingMessage,
        string completedMessage,
        Func<CancellationToken, Task> operation)
    {
        _mediaScanCancellation?.Cancel();
        _mediaScanCancellation?.Dispose();
        _mediaScanCancellation = new CancellationTokenSource();

        try
        {
            StatusMessage = startingMessage;
            await operation(_mediaScanCancellation.Token);
            StatusMessage = completedMessage;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "媒体扫描已取消。";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ApplySearchFilter()
    {
        string keyword = SearchText.Trim();
        VisibleEntries.Clear();
        foreach (AndroidFileEntry entry in Entries)
        {
            if (string.IsNullOrEmpty(keyword) || entry.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                VisibleEntries.Add(entry);
            }
        }

        if (SelectedEntry is not null && !VisibleEntries.Contains(SelectedEntry))
        {
            SelectedEntry = null;
        }
        OnPropertyChanged(nameof(ItemCountText));
    }

    private async Task GoBackAsync()
    {
        if (_backHistory.Count == 0)
        {
            return;
        }

        string target = _backHistory.Pop();
        string previous = CurrentPath;
        bool loaded = await LoadDirectoryAsync(target);
        if (loaded)
        {
            _forwardHistory.Push(previous);
        }
        else
        {
            _backHistory.Push(target);
        }

        NotifyNavigationStates();
    }

    private async Task GoForwardAsync()
    {
        if (_forwardHistory.Count == 0)
        {
            return;
        }

        string target = _forwardHistory.Pop();
        string previous = CurrentPath;
        bool loaded = await LoadDirectoryAsync(target);
        if (loaded)
        {
            _backHistory.Push(previous);
        }
        else
        {
            _forwardHistory.Push(target);
        }

        NotifyNavigationStates();
    }

    private Task GoUpAsync()
    {
        string parentPath = GetParentPath(CurrentPath);
        return NavigateToAsync(parentPath);
    }

    public static string GetParentPath(string path)
    {
        string normalizedPath = AndroidFileOutputParser.NormalizePath(path);
        if (normalizedPath == "/")
        {
            return "/";
        }

        int lastSeparator = normalizedPath.LastIndexOf('/');
        return lastSeparator <= 0 ? "/" : normalizedPath[..lastSeparator];
    }

    private bool CanLoad() => SelectedDevice?.IsOnline == true && !IsLoading;

    private bool CanOpenSelectedEntry() => CanLoad() && SelectedEntry?.IsDirectory == true;

    private bool CanDownloadSelectedFile(string? localFilePath) =>
        CanLoad() && SelectedEntry is { IsDirectory: false } && !string.IsNullOrWhiteSpace(localFilePath);

    private bool CanDownloadSelectedEntry(string? localTargetPath) =>
        CanLoad() && SelectedEntry is not null && !string.IsNullOrWhiteSpace(localTargetPath);

    private bool CanRenameSelectedEntry(string? newName) =>
        CanModifySelectedEntry() && !string.IsNullOrWhiteSpace(newName);

    private bool CanModifySelectedEntry() => CanLoad() && SelectedEntry is not null;

    private bool CanDeleteSelectedEntries() =>
        CanLoad() && SelectedEntries.Count > 0 && SelectedEntries.All(entry => !entry.IsDirectory);

    private bool CanScanSelectedMedia() => CanLoad() && SelectedEntry?.IsMediaFile == true;

    private bool CanUploadFile(string? localFilePath) =>
        CanLoad() && !string.IsNullOrWhiteSpace(localFilePath);

    private bool CanCreateDirectory(string? directoryName) =>
        CanLoad() && !string.IsNullOrWhiteSpace(directoryName);

    private static bool IsMediaFileName(string fileName) => AndroidFileEntry.IsMediaFileName(fileName);

    private void OnUploadCompleted(object? sender, UploadItem item)
    {
        if (_disposed || SelectedDevice?.IsOnline != true ||
            !string.Equals(SelectedDevice.Serial, item.Serial, StringComparison.Ordinal) ||
            !string.Equals(CurrentPath, AndroidFileOutputParser.NormalizePath(item.TargetPath), StringComparison.Ordinal))
        {
            return;
        }

        _ = RefreshAsync();
    }

    partial void OnSelectedDeviceChanged(AndroidDevice? value)
    {
        OnPropertyChanged(nameof(BreadcrumbText));
        NotifyNavigationStates();
    }

    partial void OnSelectedEntryChanged(AndroidFileEntry? value)
    {
        OpenSelectedEntryCommand.NotifyCanExecuteChanged();
        DownloadSelectedFileCommand.NotifyCanExecuteChanged();
        DownloadSelectedEntryCommand.NotifyCanExecuteChanged();
        RenameSelectedEntryCommand.NotifyCanExecuteChanged();
        DeleteSelectedEntryCommand.NotifyCanExecuteChanged();
        DeleteSelectedEntriesCommand.NotifyCanExecuteChanged();
        ScanSelectedMediaCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    partial void OnCurrentPathChanged(string value)
    {
        OnPropertyChanged(nameof(BreadcrumbText));
        NotifyNavigationStates();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ItemCountText));
        NotifyNavigationStates();
    }

    private void NotifyNavigationStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
        OpenSelectedEntryCommand.NotifyCanExecuteChanged();
        DownloadSelectedFileCommand.NotifyCanExecuteChanged();
        UploadFileCommand.NotifyCanExecuteChanged();
        CreateDirectoryCommand.NotifyCanExecuteChanged();
        DownloadSelectedEntryCommand.NotifyCanExecuteChanged();
        RenameSelectedEntryCommand.NotifyCanExecuteChanged();
        DeleteSelectedEntryCommand.NotifyCanExecuteChanged();
        ScanSelectedMediaCommand.NotifyCanExecuteChanged();
        ScanMediaDirectoryCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = null;
        if (_ownsUploadQueue) UploadQueue.Dispose();
        _createDirectoryCancellation?.Cancel();
        _createDirectoryCancellation?.Dispose();
        _createDirectoryCancellation = null;
        _entryOperationCancellation?.Cancel();
        _entryOperationCancellation?.Dispose();
        _entryOperationCancellation = null;
        _mediaScanCancellation?.Cancel();
        _mediaScanCancellation?.Dispose();
        _mediaScanCancellation = null;
        UploadQueue.UploadCompleted -= OnUploadCompleted;
    }
}
