using Microsoft.Win32;

namespace MelhorWindows.Domain.Entities;

public sealed record RegistryChangeAuditEntry(
    Guid Id,
    Guid ChangedByUserId,
    RegistryHive Hive,
    string KeyPath,
    string ValueName,
    RegistryValueKind ValueKind,
    string? PreviousValue,
    string? NewValue,
    bool WasDeleted,
    DateTimeOffset ChangedAtUtc);

