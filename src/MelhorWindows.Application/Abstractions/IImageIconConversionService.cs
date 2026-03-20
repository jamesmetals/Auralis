using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface IImageIconConversionService
{
    Task<PreparedIconAsset> PrepareIconAsync(PrepareIconRequest request, CancellationToken cancellationToken = default);
}

