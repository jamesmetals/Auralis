namespace MelhorWindows.Application.Models;

public enum RecommendationType
{
    Steam,
    Nvidia,
    Windows,
    General
}

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
    string? RelatedOptimizationId,
    RecommendationType Type = RecommendationType.General);
