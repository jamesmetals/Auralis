using System.Text.Json;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Entities;

namespace MelhorWindows.Infrastructure.Storage;

public sealed class JsonRegistryAuditRepository(AppDataPaths appDataPaths) : IRegistryAuditRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task AddRangeAsync(
        IReadOnlyCollection<RegistryChangeAuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var currentEntries = (await ReadAllUnsafeAsync(cancellationToken)).ToList();
            currentEntries.AddRange(entries);

            appDataPaths.EnsureCreated();

            await using var stream = File.Create(appDataPaths.RegistryAuditFilePath);
            await JsonSerializer.SerializeAsync(stream, currentEntries, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RegistryChangeAuditEntry>> GetRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAllUnsafeAsync(cancellationToken))
                .OrderByDescending(entry => entry.ChangedAtUtc)
                .Take(Math.Max(take, 1))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<RegistryChangeAuditEntry>> ReadAllUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appDataPaths.RegistryAuditFilePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(appDataPaths.RegistryAuditFilePath);
        var entries = await JsonSerializer.DeserializeAsync<List<RegistryChangeAuditEntry>>(
            stream,
            SerializerOptions,
            cancellationToken);

        return entries ?? [];
    }
}
