using Microsoft.Win32;

namespace MelhorWindows.Application.Models;

public sealed record GameBoosterSessionSnapshot(
    Guid SessionId,
    DateTimeOffset AppliedAtUtc,
    string? RestorePointDescription,
    IReadOnlyList<GameBoosterOptimizationSnapshot> Optimizations);

public sealed record GameBoosterOptimizationSnapshot(
    string OptimizationId,
    string Title,
    IReadOnlyList<GameBoosterRegistryValueSnapshot> Values);

public sealed record GameBoosterRegistryValueSnapshot(
    RegistryHive Hive,
    string KeyPath,
    string ValueName,
    int? PreviousValue,
    bool ValueExisted);
