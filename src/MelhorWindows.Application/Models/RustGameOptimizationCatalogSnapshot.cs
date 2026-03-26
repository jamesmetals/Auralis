namespace MelhorWindows.Application.Models;

public sealed record RustGameOptimizationCatalogSnapshot(
    RustGameProfileSnapshot Profile,
    IReadOnlyList<RustGameOptimizationState> Optimizations,
    DateTimeOffset? LastAppliedAtUtc);
