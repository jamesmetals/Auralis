using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface IWindowsRestorePointService
{
    Task<OperationResult> CreateRestorePointAsync(
        string description,
        CancellationToken cancellationToken = default);
}
