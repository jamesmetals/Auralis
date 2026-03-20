using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;
using MelhorWindows.Domain.Entities;
using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.Registry;

public sealed class WindowsRegistryEditingService(
    IAuthorizationService authorizationService,
    IUserContext userContext) : IRegistryEditingService
{
    public Task<IReadOnlyList<RegistryChangeAuditEntry>> ApplyChangesAsync(
        IReadOnlyCollection<RegistryChangeRequest> changes,
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var auditEntries = new List<RegistryChangeAuditEntry>(changes.Count);

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var baseKey = RegistryKey.OpenBaseKey(change.Hive, RegistryView.Default);
            using var targetKey = baseKey.CreateSubKey(change.KeyPath, writable: true);

            var previousValue = targetKey?.GetValue(change.ValueName);
            var previousValueText = ConvertRegistryValueToString(previousValue);

            if (change.DeleteValue)
            {
                targetKey?.DeleteValue(change.ValueName, throwOnMissingValue: false);
            }
            else
            {
                targetKey?.SetValue(change.ValueName, change.Value ?? string.Empty, change.ValueKind);
            }

            auditEntries.Add(
                new RegistryChangeAuditEntry(
                    Guid.NewGuid(),
                    userContext.UserId,
                    change.Hive,
                    change.KeyPath,
                    change.ValueName,
                    change.ValueKind,
                    previousValueText,
                    change.DeleteValue ? null : ConvertRegistryValueToString(change.Value),
                    change.DeleteValue,
                    DateTimeOffset.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<RegistryChangeAuditEntry>>(auditEntries);
    }

    private static string? ConvertRegistryValueToString(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            string[] array => string.Join("; ", array),
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value)
        };
}
