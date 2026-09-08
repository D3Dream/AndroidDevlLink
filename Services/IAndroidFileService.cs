using AndroidDevLink.Models;

namespace AndroidDevLink.Services;

public interface IAndroidFileService
{
    Task<IReadOnlyList<AndroidFileEntry>> ListDirectoryAsync(
        string serial,
        string directoryPath,
        CancellationToken cancellationToken);

    Task PullFileAsync(
        string serial,
        string remoteFilePath,
        string localFilePath,
        CancellationToken cancellationToken);

    Task PushFileAsync(
        string serial,
        string localFilePath,
        string remoteDirectoryPath,
        CancellationToken cancellationToken);

    Task CreateDirectoryAsync(
        string serial,
        string parentDirectoryPath,
        string directoryName,
        CancellationToken cancellationToken);

    Task RenameEntryAsync(
        string serial,
        string sourcePath,
        string newName,
        CancellationToken cancellationToken);

    Task DeleteEntryAsync(
        string serial,
        string entryPath,
        bool isDirectory,
        CancellationToken cancellationToken);

    Task ScanMediaFileAsync(
        string serial,
        string mediaFilePath,
        CancellationToken cancellationToken);

}
