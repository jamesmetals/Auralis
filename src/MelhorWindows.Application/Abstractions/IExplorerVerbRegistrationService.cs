namespace MelhorWindows.Application.Abstractions;

public interface IExplorerVerbRegistrationService
{
    Task RegisterFolderVerbAsync(string appExecutablePath, CancellationToken cancellationToken = default);

    Task UnregisterFolderVerbAsync(CancellationToken cancellationToken = default);
}

