using System.Diagnostics;

namespace AndroidDevLink.Services;

public sealed class AdbDeviceMonitor : IDisposable
{
    private readonly string _adbExecutable;
    private readonly Action _onChanged;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _monitorTask;
    private bool _disposed;

    public AdbDeviceMonitor(string adbExecutable, Action onChanged)
    {
        _adbExecutable = adbExecutable;
        _onChanged = onChanged;
        _monitorTask = MonitorAsync();
    }

    private async Task MonitorAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _adbExecutable,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.StartInfo.ArgumentList.Add("track-devices");
                if (!process.Start())
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), _cancellation.Token);
                    continue;
                }

                try
                {
                    while (!_cancellation.IsCancellationRequested &&
                           await process.StandardOutput.ReadLineAsync(_cancellation.Token) is not null)
                    {
                        _onChanged();
                    }
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(TimeSpan.FromSeconds(3), _cancellation.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        try { _monitorTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cancellation.Dispose();
    }
}
