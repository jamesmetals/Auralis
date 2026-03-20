using Microsoft.Win32;

namespace MelhorWindows.Application.Models;

public sealed record RegistryChangeRequest(
    RegistryHive Hive,
    string KeyPath,
    string ValueName,
    object? Value,
    RegistryValueKind ValueKind,
    bool DeleteValue = false);

