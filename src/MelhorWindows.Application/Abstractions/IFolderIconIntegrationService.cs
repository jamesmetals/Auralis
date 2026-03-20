namespace MelhorWindows.Application.Abstractions;

public interface IFolderIconIntegrationService
{
    Task ApplyIconAsync(string folderPath, string iconFilePath, CancellationToken cancellationToken = default);
}

