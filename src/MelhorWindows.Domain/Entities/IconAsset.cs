namespace MelhorWindows.Domain.Entities;

public sealed record IconAsset(
    Guid Id,
    Guid UserId,
    string OriginalFileName,
    string StoredIconPath,
    string StoredPreviewImagePath,
    DateTimeOffset CreatedAtUtc);
