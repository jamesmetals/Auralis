namespace MelhorWindows.Application.Abstractions;

public interface IPersonalIconLibraryService
{
    Task<IReadOnlyList<PersonalIconEntry>> GetAllAsync();
    Task AddAsync(string displayName, string sourceIconPath, string? previewImagePath = null);
    Task RemoveAsync(Guid id);
}

public sealed record PersonalIconEntry(
    Guid Id,
    string DisplayName,
    string StoredIconPath,
    string StoredPreviewPath,
    DateTimeOffset CreatedAt);
