namespace MelhorWindows.Application.Models;

public sealed record GameBoosterAiPanelSnapshot(
    LocalAiConnectionSettings Settings,
    LocalAiAvailabilitySnapshot Availability,
    GameBoosterAiAnalysisSnapshot? LastAnalysis);
