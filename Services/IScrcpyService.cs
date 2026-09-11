namespace AndroidDevLink.Services;

public interface IScrcpyService
{
    Task StartAsync(string serial, bool noAudio, CancellationToken cancellationToken);
}
