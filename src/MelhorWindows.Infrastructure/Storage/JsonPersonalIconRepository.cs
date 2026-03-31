using System.Text.Json;
using MelhorWindows.Application.Abstractions;

namespace MelhorWindows.Infrastructure.Storage;

public sealed class JsonPersonalIconRepository : IPersonalIconLibraryService
{
    private readonly AppDataPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public JsonPersonalIconRepository(AppDataPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<PersonalIconEntry>> GetAllAsync()
    {
        if (!File.Exists(_paths.PersonalIconsFilePath))
            return Array.Empty<PersonalIconEntry>();

        var json = await File.ReadAllTextAsync(_paths.PersonalIconsFilePath);
        return JsonSerializer.Deserialize<List<PersonalIconEntry>>(json, _jsonOptions)
               ?? new List<PersonalIconEntry>();
    }

    public async Task AddAsync(string displayName, string sourceIconPath, string? previewImagePath = null)
    {
        _paths.EnsureCreated();

        var id = Guid.NewGuid();
        var ext = Path.GetExtension(sourceIconPath);
        var storedName = $"{id:N}{ext}";
        var storedIconPath = Path.Combine(_paths.PersonalIconsDirectory, storedName);
        var storedPreviewPath = Path.Combine(_paths.PersonalIconsDirectory, $"{id:N}_preview.png");

        File.Copy(sourceIconPath, storedIconPath, overwrite: true);

        var previewSource = !string.IsNullOrWhiteSpace(previewImagePath) && File.Exists(previewImagePath)
            ? previewImagePath
            : Path.ChangeExtension(sourceIconPath, ".png");

        if (File.Exists(previewSource))
            File.Copy(previewSource, storedPreviewPath, overwrite: true);
        else
            storedPreviewPath = storedIconPath;

        var entries = (await GetAllAsync()).ToList();
        entries.Insert(0, new PersonalIconEntry(id, displayName, storedIconPath, storedPreviewPath, DateTimeOffset.UtcNow));

        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        await File.WriteAllTextAsync(_paths.PersonalIconsFilePath, json);
    }

    public async Task RemoveAsync(Guid id)
    {
        var entries = (await GetAllAsync()).ToList();
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return;

        entries.Remove(entry);

        try { if (File.Exists(entry.StoredIconPath)) File.Delete(entry.StoredIconPath); } catch { }
        try { if (File.Exists(entry.StoredPreviewPath)) File.Delete(entry.StoredPreviewPath); } catch { }

        var json = JsonSerializer.Serialize(entries, _jsonOptions);
        await File.WriteAllTextAsync(_paths.PersonalIconsFilePath, json);
    }
}
