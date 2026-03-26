namespace MelhorWindows.Infrastructure.AI;

internal sealed record GameOptimizationKnowledgePack(
    string GameKey,
    string GameTitle,
    string ResearchDate,
    string ResearchSummary,
    IReadOnlyList<GameOptimizationKnowledgeSource> Sources,
    IReadOnlyList<string> CorePrinciples,
    IReadOnlyList<string> MythsToAvoid,
    IReadOnlyList<GameOptimizationKnowledgeRule> RecommendationRules);

internal sealed record GameOptimizationKnowledgeSource(
    string Label,
    string Url,
    string EvidenceLevel);

internal sealed record GameOptimizationKnowledgeRule(
    string Id,
    string Priority,
    string RuleType,
    string Title,
    string Recommendation,
    string Reason,
    string Applicability,
    string Confidence,
    GameOptimizationKnowledgeConditions? Conditions);

internal sealed record GameOptimizationKnowledgeConditions(
    int? MinRamGb,
    int? MaxRamGb,
    int? MinMemoryLoadPercent,
    int? MaxMemoryLoadPercent,
    IReadOnlyList<string>? CpuContainsAny,
    IReadOnlyList<string>? CpuExcludesAny,
    IReadOnlyList<string>? GpuVendorsAny,
    IReadOnlyList<string>? WindowsContainsAny);
