using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Application.Abstractions;

public interface IRegistryEditingService
{
    Task<IReadOnlyList<RegistryChangeAuditEntry>> ApplyChangesAsync(
        IReadOnlyCollection<RegistryChangeRequest> changes,
        CancellationToken cancellationToken = default);
}

