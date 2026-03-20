using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Application.Abstractions;

public interface IIconHistoryRepository
{
    Task<IReadOnlyList<FolderIconHistoryEntry>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(FolderIconHistoryEntry entry, CancellationToken cancellationToken = default);
}

