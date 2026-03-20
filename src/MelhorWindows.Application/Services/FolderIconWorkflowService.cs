using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Application.Services;

public sealed class FolderIconWorkflowService(
    IAuthorizationService authorizationService,
    IUserContext userContext,
    IImageIconConversionService imageIconConversionService,
    IIconStorageService iconStorageService,
    IFolderIconIntegrationService folderIconIntegrationService,
    IIconHistoryRepository iconHistoryRepository)
{
    public async Task<OperationResult<FolderIconHistoryEntry>> ExecuteAsync(
        ApplyFolderIconRequest request,
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.ApplyFolderIcons);

        if (!Directory.Exists(request.FolderPath))
        {
            return OperationResult<FolderIconHistoryEntry>.Failure("The target folder does not exist.");
        }

        if (!File.Exists(request.SourceImagePath))
        {
            return OperationResult<FolderIconHistoryEntry>.Failure("The selected image does not exist.");
        }

        var preparedAsset = await imageIconConversionService.PrepareIconAsync(
            new PrepareIconRequest(
                request.SourceImagePath,
                request.FitMode,
                request.CropSelection,
                Path.GetFileNameWithoutExtension(request.SourceImagePath)),
            cancellationToken);

        var storedAsset = await iconStorageService.SaveAsync(userContext.UserId, preparedAsset, cancellationToken);

        await folderIconIntegrationService.ApplyIconAsync(
            request.FolderPath,
            storedAsset.StoredIconPath,
            cancellationToken);

        var historyEntry = new FolderIconHistoryEntry(
            Guid.NewGuid(),
            userContext.UserId,
            storedAsset.Id,
            request.FolderPath,
            storedAsset.StoredIconPath,
            request.SourceImagePath,
            storedAsset.StoredPreviewImagePath,
            request.FitMode,
            DateTimeOffset.UtcNow);

        if (request.SaveToHistory)
        {
            await iconHistoryRepository.AddAsync(historyEntry, cancellationToken);
        }

        return OperationResult<FolderIconHistoryEntry>.Success(
            historyEntry,
            "Folder icon applied successfully.");
    }
}
