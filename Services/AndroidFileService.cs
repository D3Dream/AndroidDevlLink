using System.ComponentModel;
using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public sealed class AndroidFileService : IAndroidFileService
{
    private readonly IProcessRunner _processRunner;
    private readonly string _adbExecutable;

    public AndroidFileService()
        : this(new ProcessRunner(), AdbExecutableResolver.Resolve())
    {
    }

    public AndroidFileService(IProcessRunner processRunner, string adbExecutable)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _adbExecutable = string.IsNullOrWhiteSpace(adbExecutable)
            ? throw new ArgumentException("ADB 可执行文件路径不能为空。", nameof(adbExecutable))
            : adbExecutable;
    }

    public async Task<IReadOnlyList<AndroidFileEntry>> ListDirectoryAsync(
        string serial,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalizedPath = AndroidFileOutputParser.NormalizePath(directoryPath);

        ProcessResult result;
        try
        {
            // 目录路径末尾保留斜杠，避免 /sdcard 等符号链接只返回链接本身而不列出目标目录内容。
            string directoryArgument = normalizedPath == "/" ? "/" : normalizedPath + "/";
            result = await _processRunner.RunAsync(
                _adbExecutable,
                ["-s", serial, "shell", "ls", "-la", directoryArgument],
                cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "未找到 adb.exe。请安装 Android SDK Platform-Tools，并将其目录加入 PATH。",
                exception);
        }

        if (result.ExitCode != 0)
        {
            string details = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException($"读取 Android 目录失败：{details.Trim()}");
        }

        return AndroidFileOutputParser.ParseDirectory(normalizedPath, result.StandardOutput);
    }

    public async Task PullFileAsync(
        string serial,
        string remoteFilePath,
        string localFilePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        ProcessResult result = await RunFileTransferAsync(
            ["-s", serial, "pull", remoteFilePath, localFilePath],
            "下载",
            cancellationToken);
        ThrowIfTransferFailed(result, "下载");
    }

    public async Task PushFileAsync(
        string serial,
        string localFilePath,
        string remoteDirectoryPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);
        string normalizedDirectory = AndroidFileOutputParser.NormalizePath(remoteDirectoryPath);
        string directoryArgument = normalizedDirectory == "/" ? "/" : normalizedDirectory + "/";

        ProcessResult result = await RunFileTransferAsync(
            ["-s", serial, "push", localFilePath, directoryArgument],
            "上传",
            cancellationToken);
        ThrowIfTransferFailed(result, "上传");
    }

    public async Task CreateDirectoryAsync(
        string serial,
        string parentDirectoryPath,
        string directoryName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string validDirectoryName = ValidateDirectoryName(directoryName);
        string normalizedParent = AndroidFileOutputParser.NormalizePath(parentDirectoryPath);
        string targetPath = AndroidFileOutputParser.CombineAndroidPath(normalizedParent, validDirectoryName);
        string shellCommand = $"mkdir -- {QuoteShellArgument(targetPath)}";

        ProcessResult result = await RunAdbOperationAsync(
            ["-s", serial, "shell", shellCommand],
            "新建 Android 文件夹",
            cancellationToken);
        ThrowIfOperationFailed(result, "新建 Android 文件夹失败");
    }

    public async Task RenameEntryAsync(
        string serial,
        string sourcePath,
        string newName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalizedSource = AndroidFileOutputParser.NormalizePath(sourcePath);
        if (normalizedSource == "/")
        {
            throw new ArgumentException("不能重命名 Android 根目录。", nameof(sourcePath));
        }

        string validName = ValidateEntryName(newName);
        string parentPath = GetParentPath(normalizedSource);
        string targetPath = AndroidFileOutputParser.CombineAndroidPath(parentPath, validName);
        string shellCommand = $"mv -- {QuoteShellArgument(normalizedSource)} {QuoteShellArgument(targetPath)}";

        ProcessResult result = await RunAdbOperationAsync(
            ["-s", serial, "shell", shellCommand],
            "重命名 Android 项目",
            cancellationToken);
        ThrowIfOperationFailed(result, "重命名 Android 项目失败");
    }

    public async Task DeleteEntryAsync(
        string serial,
        string entryPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalizedPath = AndroidFileOutputParser.NormalizePath(entryPath);
        if (normalizedPath == "/")
        {
            throw new ArgumentException("不能删除 Android 根目录。", nameof(entryPath));
        }

        string removeOption = isDirectory ? "-rf" : "-f";
        string shellCommand = $"rm {removeOption} -- {QuoteShellArgument(normalizedPath)}";
        ProcessResult result = await RunAdbOperationAsync(
            ["-s", serial, "shell", shellCommand],
            "删除 Android 项目",
            cancellationToken);
        ThrowIfOperationFailed(result, "删除 Android 项目失败");
    }

    public async Task ScanMediaFileAsync(
        string serial,
        string mediaFilePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string normalizedPath = AndroidFileOutputParser.NormalizePath(mediaFilePath);
        ProcessResult result = await RunMediaScanBroadcastAsync(serial, normalizedPath, cancellationToken);
        ThrowIfOperationFailed(result, "扫描 Android 媒体文件失败");
    }

    public static string ValidateDirectoryName(string directoryName) => ValidateEntryName(directoryName);

    public static string ValidateEntryName(string entryName)
    {
        string name = entryName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("名称不能为空。", nameof(entryName));
        }

        if (name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("名称不能为 . 或 ..，且不能包含 /、\\ 或换行符。", nameof(entryName));
        }

        return name;
    }

    private static string GetParentPath(string path)
    {
        int lastSeparator = path.LastIndexOf('/');
        return lastSeparator <= 0 ? "/" : path[..lastSeparator];
    }

    private static string QuoteShellArgument(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private Task<ProcessResult> RunMediaScanBroadcastAsync(
        string serial,
        string path,
        CancellationToken cancellationToken) =>
        RunAdbOperationAsync(
            [
                "-s", serial, "shell", "am", "broadcast",
                "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
                "-d", CreateFileUri(path)
            ],
            "扫描 Android 媒体文件",
            cancellationToken);

    private static string CreateFileUri(string path) => new UriBuilder(Uri.UriSchemeFile, string.Empty)
    {
        Path = path
    }.Uri.AbsoluteUri;

    private Task<ProcessResult> RunFileTransferAsync(
        IReadOnlyList<string> arguments,
        string operationName,
        CancellationToken cancellationToken) =>
        RunAdbOperationAsync(arguments, $"{operationName} Android 文件", cancellationToken);

    private async Task<ProcessResult> RunAdbOperationAsync(
        IReadOnlyList<string> arguments,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(_adbExecutable, arguments, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"未找到 adb.exe，无法{operationName}。请安装 Android SDK Platform-Tools，并将其目录加入 PATH。",
                exception);
        }
    }

    private static void ThrowIfTransferFailed(ProcessResult result, string operationName) =>
        ThrowIfOperationFailed(result, $"{operationName} Android 文件失败");

    private static void ThrowIfOperationFailed(ProcessResult result, string errorPrefix)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        string details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        throw new InvalidOperationException($"{errorPrefix}：{details.Trim()}");
    }
}
