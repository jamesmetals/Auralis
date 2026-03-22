namespace MelhorWindows.Application.Models;

public sealed record RustGameBoosterAiAnalysisSnapshot(
    DateTimeOffset GeneratedAtUtc,
    string EndpointUrl,
    string ModelName,
    string ExecutiveSummary,
    string LaunchOptionsSummary,
    IReadOnlyList<GameBoosterAiRecommendation> Recommendations);
