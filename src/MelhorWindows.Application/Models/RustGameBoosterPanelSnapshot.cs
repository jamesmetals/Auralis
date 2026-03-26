namespace MelhorWindows.Application.Models;

public sealed record RustGameBoosterPanelSnapshot(
    RustGameProfileSnapshot Profile,
    IReadOnlyList<RustGameOptimizationState> Optimizations,
    DateTimeOffset? LastAppliedAtUtc,
    RustGameBoosterAiAnalysisSnapshot? LastAnalysis);
