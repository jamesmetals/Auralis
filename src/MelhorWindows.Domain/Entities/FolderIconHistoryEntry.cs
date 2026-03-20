using MelhorWindows.Domain.Enums;

namespace MelhorWindows.Domain.Entities;

public sealed record FolderIconHistoryEntry(
    Guid Id,
    Guid UserId,
    Guid IconAssetId,
    string FolderPath,
    string StoredIconPath,
    string SourceImagePath,
    string? StoredPreviewImagePath,
    ImageFitMode FitMode,
    DateTimeOffset AppliedAtUtc);
