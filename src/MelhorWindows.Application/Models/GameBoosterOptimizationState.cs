namespace MelhorWindows.Application.Models;

public sealed record GameBoosterOptimizationState(
    string Id,
    string Title,
    string Category,
    string Description,
    string CurrentLabel,
    string RecommendedLabel,
    string ImpactLabel,
    string RiskLabel,
    bool IsOptimized,
    bool RequiresRestart);
