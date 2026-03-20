using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Infrastructure.Storage;

public sealed class FileSystemIconStorageService(AppDataPaths appDataPaths) : IIconStorageService
{
    public async Task<IconAsset> SaveAsync(
        Guid userId,
        PreparedIconAsset asset,
        CancellationToken cancellationToken = default)
    {
        appDataPaths.EnsureCreated();

        var sanitizedName = SanitizeFileName(asset.SuggestedFileName);
        var fileStem = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{userId:N}-{sanitizedName}";
        var iconFileName = $"{fileStem}.ico";
        var previewFileName = $"{fileStem}.png";
        var storedPath = Path.Combine(appDataPaths.IconCacheDirectory, iconFileName);
        var previewPath = Path.Combine(appDataPaths.PreviewCacheDirectory, previewFileName);

        await File.WriteAllBytesAsync(storedPath, asset.IconBytes, cancellationToken);
        await File.WriteAllBytesAsync(previewPath, asset.PreviewPngBytes, cancellationToken);

        return new IconAsset(
            Guid.NewGuid(),
            userId,
            asset.OriginalFileName,
            storedPath,
            previewPath,
            DateTimeOffset.UtcNow);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedCharacters = fileName
            .Select(character => invalidChars.Contains(character) ? '-' : character)
            .ToArray();

        return new string(sanitizedCharacters).Trim('-');
    }
}
