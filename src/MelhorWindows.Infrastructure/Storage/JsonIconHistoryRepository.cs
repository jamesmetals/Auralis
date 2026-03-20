using System.Text.Json;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Infrastructure.Storage;

public sealed class JsonIconHistoryRepository(AppDataPaths appDataPaths) : IIconHistoryRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // History can grow; don't pay the extra CPU+I/O cost for indentation.
        WriteIndented = false
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<FolderIconHistoryEntry>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var allEntries = await ReadAllUnsafeAsync(cancellationToken);

            return allEntries
                .Where(entry => entry.UserId == userId)
                .OrderByDescending(entry => entry.AppliedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(FolderIconHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var allEntries = (await ReadAllUnsafeAsync(cancellationToken)).ToList();
            allEntries.Add(entry);

            appDataPaths.EnsureCreated();

            await using var stream = File.Create(appDataPaths.HistoryFilePath);
            await JsonSerializer.SerializeAsync(stream, allEntries, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<FolderIconHistoryEntry>> ReadAllUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appDataPaths.HistoryFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(appDataPaths.HistoryFilePath);
        var entries = await JsonSerializer.DeserializeAsync<List<FolderIconHistoryEntry>>(
            stream,
            SerializerOptions,
            cancellationToken);

        return entries ?? [];
    }
}

