namespace AndroidDevLink.Services;

public interface IDetachedProcessLauncher
{
    void Start(string executable, IReadOnlyList<string> arguments, string workingDirectory);
}
