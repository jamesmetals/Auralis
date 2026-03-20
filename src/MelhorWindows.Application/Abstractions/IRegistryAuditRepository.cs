using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Application.Abstractions;

public interface IRegistryAuditRepository
{
    Task AddRangeAsync(
        IReadOnlyCollection<RegistryChangeAuditEntry> entries,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RegistryChangeAuditEntry>> GetRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default);
}

