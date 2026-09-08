namespace AndroidDevLink.Services;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
