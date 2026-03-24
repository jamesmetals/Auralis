using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface IComputerDiagnosticsService
{
    Task<ComputerDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
