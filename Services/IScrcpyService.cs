namespace AndroidDevLink.Services;

public interface IScrcpyService
{
    Task StartAsync(string serial, CancellationToken cancellationToken);
}
