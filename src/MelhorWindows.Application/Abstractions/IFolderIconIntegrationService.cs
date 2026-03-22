namespace MelhorWindows.Application.Abstractions;

public interface IFolderIconIntegrationService
{
    Task ApplyIconAsync(string folderPath, string iconFilePath, CancellationToken cancellationToken = default);
    Task RemoveIconAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<bool> RepairIconReferenceAsync(string folderPath, CancellationToken cancellationToken = default);
}
