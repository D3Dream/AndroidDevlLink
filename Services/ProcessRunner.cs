using System.Diagnostics;
using System.IO;

namespace AndroidDevLink.Services;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // ArgumentList 会处理空格和引号，避免文件路径被命令行错误拆分。
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"无法启动 {Path.GetFileName(executable)}。");
            }

            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new ProcessResult(process.ExitCode, await standardOutputTask, await standardErrorTask);
        }
        catch (OperationCanceledException)
        {
            // 取消操作时同时终止子进程，避免后台传输继续占用设备。
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* Process exited during cancellation. */ }
            await process.WaitForExitAsync(CancellationToken.None);

            throw;
        }
    }
}
