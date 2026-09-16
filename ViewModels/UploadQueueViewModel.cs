using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AndroidDevLink.ViewModels;

public enum UploadState { Waiting, Uploading, Finishing, Completed, Failed, Cancelled }

public sealed class UploadItem : ObservableObject
{
    private UploadState _state = UploadState.Waiting;
    private string _status = "等待上传";
    public string LocalPath { get; }
    public string FileName => Path.GetFileName(LocalPath);
    public string Serial { get; }
    public string TargetPath { get; }
    public UploadState State => _state;
    public string Status => _status;
    public bool IsActive => _state is UploadState.Uploading or UploadState.Finishing;
    public bool CanCancel => _state is UploadState.Waiting or UploadState.Uploading;
    internal Func<CancellationToken, Task> Transfer { get; }
    internal Func<CancellationToken, Task>? AfterUpload { get; }

    internal UploadItem(string serial, string localPath, string targetPath,
        Func<CancellationToken, Task> transfer, Func<CancellationToken, Task>? afterUpload)
    {
        Serial = serial;
        LocalPath = localPath;
        TargetPath = targetPath;
        Transfer = transfer;
        AfterUpload = afterUpload;
    }

    internal void Update(UploadState state, string status)
    {
        _state = state;
        _status = status;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanCancel));
    }
}

// All queue entry points run on the WPF UI thread; await keeps that context.
// File I/O and transfer startup run on a worker; queue and binding updates stay on the UI thread.
public sealed class UploadQueueViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<UploadItem> _items = [];
    private readonly Queue<UploadItem> _pending = new();
    private UploadItem? _current;
    private CancellationTokenSource? _currentCancellation;
    private bool _processing;
    private bool _disposed;
    public ReadOnlyObservableCollection<UploadItem> Items { get; }
    public Task ProcessingTask { get; private set; } = Task.CompletedTask;
    public event EventHandler<UploadItem>? UploadCompleted;
    public IRelayCommand<UploadItem> CancelCommand { get; }
    public IRelayCommand ClearWaitingCommand { get; }
    public IRelayCommand ClearFinishedCommand { get; }
    public string Summary => $"上传队列 · 进行中 {_items.Count(x => x.IsActive)} · 等待 {_items.Count(x => x.State == UploadState.Waiting)}";

    public UploadQueueViewModel()
    {
        Items = new(_items);
        CancelCommand = new RelayCommand<UploadItem>(Cancel, item => item?.CanCancel == true && !_disposed);
        ClearWaitingCommand = new RelayCommand(ClearWaiting);
        ClearFinishedCommand = new RelayCommand(() =>
        {
            foreach (var item in _items.Where(x => !x.IsActive && x.State != UploadState.Waiting).ToArray())
                _items.Remove(item);
            Notify();
        });
    }

    public void Enqueue(string serial, string localPath, string targetPath,
        Func<CancellationToken, Task> transfer, Func<CancellationToken, Task>? afterUpload = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentNullException.ThrowIfNull(transfer);
        var item = new UploadItem(serial, Path.GetFullPath(localPath), targetPath, transfer, afterUpload);
        _items.Add(item);
        _pending.Enqueue(item);
        Notify();
        if (!_processing) ProcessingTask = ProcessAsync();
    }

    private async Task ProcessAsync()
    {
        _processing = true;
        try
        {
            while (!_disposed && _pending.TryDequeue(out var item))
            {
                if (item.State != UploadState.Waiting) continue;
                using var cancellation = new CancellationTokenSource();
                _current = item;
                _currentCancellation = cancellation;
                item.Update(UploadState.Uploading, "正在上传…");
                Notify();
                try
                {
                    await Task.Run(async () =>
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (!File.Exists(item.LocalPath))
                            throw new FileNotFoundException("上传源文件不存在或已移走。", item.LocalPath);
                        cancellation.Token.ThrowIfCancellationRequested();
                        await item.Transfer(cancellation.Token).ConfigureAwait(false);
                    }, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    item.Update(UploadState.Finishing, "已上传，正在处理媒体索引…");
                    Notify();
                    try
                    {
                        if (item.AfterUpload is not null)
                            await Task.Run(() => item.AfterUpload(cancellation.Token), cancellation.Token);
                        item.Update(UploadState.Completed, "上传完成");
                        UploadCompleted?.Invoke(this, item);
                    }
                    catch (Exception ex)
                    {
                        item.Update(UploadState.Completed, $"已上传；后续处理未完成：{ex.Message}");
                    }
                }
                catch (OperationCanceledException)
                {
                    item.Update(UploadState.Cancelled, "已取消，设备可能保留未完整文件");
                }
                catch (Exception ex)
                {
                    item.Update(UploadState.Failed, $"上传失败：{ex.Message}");
                }
                finally
                {
                    _current = null;
                    _currentCancellation = null;
                    Notify();
                }
            }
        }
        finally { _processing = false; }
    }

    private void Cancel(UploadItem? item)
    {
        if (item?.CanCancel != true) return;
        if (ReferenceEquals(item, _current))
        {
            _currentCancellation?.Cancel();
        }
        else item.Update(UploadState.Cancelled, "已取消排队");
        Notify();
    }

    private void ClearWaiting()
    {
        foreach (var item in _pending)
            if (item.State == UploadState.Waiting) item.Update(UploadState.Cancelled, "已取消排队");
        _pending.Clear();
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Summary));
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearWaiting();
        _currentCancellation?.Cancel();
    }
}
