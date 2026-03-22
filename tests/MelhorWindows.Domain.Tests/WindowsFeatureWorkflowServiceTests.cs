using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Application.Services;
using MelhorWindows.Domain.Entities;
using Microsoft.Win32;
using Xunit;

namespace MelhorWindows.Domain.Tests;

public sealed class WindowsFeatureWorkflowServiceTests
{
    [Fact]
    public async Task SetStateAsync_WritesAuditAndReturnsEnabledState()
    {
        var registryEditingService = new FakeRegistryEditingService();
        var registryInspectionService = new FakeRegistryInspectionService();
        var registryAuditRepository = new FakeRegistryAuditRepository();
        var service = new WindowsFeatureWorkflowService(
            new AllowAllAuthorizationService(),
            registryEditingService,
            registryInspectionService,
            registryAuditRepository,
            new FakeProtectedStateStore(),
            new FakeWindowsRestorePointService());

        var feature = WindowsFeatureCatalog.DefaultFeatures.First();

        foreach (var change in feature.EnableChanges)
        {
            registryInspectionService.SetValue(change.Hive, change.KeyPath, change.ValueName, change.Value);
        }

        var result = await service.SetStateAsync(feature.Id, enabled: true);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(WindowsFeatureStatus.Enabled, result.Value!.Status);
        Assert.NotEmpty(registryEditingService.AppliedChanges);
        Assert.NotEmpty(registryAuditRepository.StoredEntries);
    }

    [Fact]
    public async Task GetStatesAsync_ReturnsDisabledWhenRegistryMatchesDisabledDefinition()
    {
        var feature = WindowsFeatureCatalog.DefaultFeatures.First();
        var registryInspectionService = new FakeRegistryInspectionService();

        foreach (var change in feature.DisableChanges)
        {
            registryInspectionService.SetValue(change.Hive, change.KeyPath, change.ValueName, change.Value);
        }

        var service = new WindowsFeatureWorkflowService(
            new AllowAllAuthorizationService(),
            new FakeRegistryEditingService(),
            registryInspectionService,
            new FakeRegistryAuditRepository(),
            new FakeProtectedStateStore(),
            new FakeWindowsRestorePointService());

        var states = await service.GetStatesAsync();
        var state = states.First(item => item.Id == feature.Id);

        Assert.Equal(WindowsFeatureStatus.Disabled, state.Status);
    }

    private sealed class AllowAllAuthorizationService : IAuthorizationService
    {
        public bool HasPermission(string permission) => true;

        public void EnsurePermission(string permission)
        {
        }
    }

    private sealed class FakeRegistryEditingService : IRegistryEditingService
    {
        public List<RegistryChangeRequest> AppliedChanges { get; } = [];

        public Task<IReadOnlyList<RegistryChangeAuditEntry>> ApplyChangesAsync(
            IReadOnlyCollection<RegistryChangeRequest> changes,
            CancellationToken cancellationToken = default)
        {
            AppliedChanges.AddRange(changes);

            IReadOnlyList<RegistryChangeAuditEntry> auditEntries = changes
                .Select(change => new RegistryChangeAuditEntry(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    change.Hive,
                    change.KeyPath,
                    change.ValueName,
                    change.ValueKind,
                    null,
                    change.Value?.ToString(),
                    change.DeleteValue,
                    DateTimeOffset.UtcNow))
                .ToArray();

            return Task.FromResult(auditEntries);
        }
    }

    private sealed class FakeRegistryInspectionService : IRegistryInspectionService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<object?> GetValueAsync(
            RegistryHive hive,
            string keyPath,
            string valueName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.TryGetValue(BuildKey(hive, keyPath, valueName), out var value);
            return Task.FromResult(value);
        }

        public void SetValue(RegistryHive hive, string keyPath, string valueName, object? value)
        {
            _values[BuildKey(hive, keyPath, valueName)] = value;
        }

        private static string BuildKey(RegistryHive hive, string keyPath, string valueName) =>
            $"{hive}|{keyPath}|{valueName}";
    }

    private sealed class FakeRegistryAuditRepository : IRegistryAuditRepository
    {
        public List<RegistryChangeAuditEntry> StoredEntries { get; } = [];

        public Task AddRangeAsync(
            IReadOnlyCollection<RegistryChangeAuditEntry> entries,
            CancellationToken cancellationToken = default)
        {
            StoredEntries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegistryChangeAuditEntry>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RegistryChangeAuditEntry> entries = StoredEntries
                .Take(take)
                .ToArray();

            return Task.FromResult(entries);
        }
    }

    private sealed class FakeProtectedStateStore : IProtectedStateStore
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) &&
                value is T typedValue)
            {
                return Task.FromResult<T?>(typedValue);
            }

            return Task.FromResult<T?>(default);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWindowsRestorePointService : IWindowsRestorePointService
    {
        public Task<OperationResult> CreateRestorePointAsync(
            string description,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult.Success(description));
        }
    }
}
