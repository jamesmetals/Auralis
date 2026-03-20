using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface IAppUpdateService
{
    Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
