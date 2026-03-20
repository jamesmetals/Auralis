using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Application.Abstractions;

public interface IIconStorageService
{
    Task<IconAsset> SaveAsync(Guid userId, PreparedIconAsset asset, CancellationToken cancellationToken = default);
}

