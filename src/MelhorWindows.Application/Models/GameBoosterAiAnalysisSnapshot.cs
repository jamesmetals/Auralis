namespace MelhorWindows.Application.Models;

public sealed record GameBoosterAiAnalysisSnapshot(
    DateTimeOffset GeneratedAtUtc,
    string EndpointUrl,
    string ModelName,
    string ExecutiveSummary,
    string RecommendedProfile,
    string ReadinessLevel,
    IReadOnlyList<GameBoosterAiRecommendation> Recommendations);

public sealed record GameBoosterAiRecommendation(
    string Priority,
    string Title,
    string Reason,
    string SuggestedAction,
    string? RelatedOptimizationId);
