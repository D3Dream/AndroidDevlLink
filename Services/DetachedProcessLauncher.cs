using System.Diagnostics;
using System.IO;

namespace AndroidDevLink.Services;

public sealed class DetachedProcessLauncher : IDetachedProcessLauncher
{
    public void Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"无法启动 {Path.GetFileName(executable)}。");
        }

        // 释放托管句柄不会终止已启动的独立进程。
        process.Dispose();
    }
}
