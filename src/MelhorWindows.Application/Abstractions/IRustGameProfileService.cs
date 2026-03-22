using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface IRustGameProfileService
{
    Task<RustGameProfileSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
