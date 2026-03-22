namespace MelhorWindows.Application.Models;

public sealed record GameBoosterDashboardSnapshot(
    string ActiveProfileName,
    int OptimizationScore,
    int OptimizedItemCount,
    int TotalItemCount,
    OptimizationSafetySettings SafetySettings,
    DateTimeOffset? LastAppliedAtUtc,
    string? LastRestorePointDescription,
    IReadOnlyList<GameBoosterOptimizationState> Optimizations);
