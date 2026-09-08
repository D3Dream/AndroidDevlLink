using System.ComponentModel;
using System.IO;

namespace AndroidDevLink.Services;

public sealed class ScrcpyService : IScrcpyService
{
    private readonly IDetachedProcessLauncher _processLauncher;
    private readonly string _scrcpyExecutable;

    public ScrcpyService()
        : this(new DetachedProcessLauncher(), ResolveScrcpyExecutable())
    {
    }

    public ScrcpyService(IDetachedProcessLauncher processLauncher, string scrcpyExecutable)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _scrcpyExecutable = string.IsNullOrWhiteSpace(scrcpyExecutable)
            ? throw new ArgumentException("scrcpy 可执行文件路径不能为空。", nameof(scrcpyExecutable))
            : scrcpyExecutable;
    }

    public Task StartAsync(string serial, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _processLauncher.Start(
                _scrcpyExecutable,
                ["-s", serial],
                GetWorkingDirectory(_scrcpyExecutable));
            return Task.CompletedTask;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "未找到 scrcpy.exe。请安装 scrcpy 并将其目录加入 PATH，或将 scrcpy 放入应用目录。",
                exception);
        }
    }

    private static string ResolveScrcpyExecutable()
    {
        string bundledPath = Path.Combine(AppContext.BaseDirectory, "scrcpy.exe");
        return File.Exists(bundledPath) ? bundledPath : "scrcpy.exe";
    }

    private static string GetWorkingDirectory(string executable)
    {
        string? directory = Path.GetDirectoryName(executable);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : AppContext.BaseDirectory;
    }
}
