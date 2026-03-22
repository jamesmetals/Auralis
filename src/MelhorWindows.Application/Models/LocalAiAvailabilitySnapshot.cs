namespace MelhorWindows.Application.Models;

public sealed record LocalAiAvailabilitySnapshot(
    bool IsReachable,
    string StatusMessage,
    IReadOnlyList<string> AvailableModels,
    bool ConfiguredModelAvailable,
    string? SuggestedPullCommand);
