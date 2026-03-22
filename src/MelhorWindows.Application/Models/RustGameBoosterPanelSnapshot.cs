namespace MelhorWindows.Application.Models;

public sealed record RustGameBoosterPanelSnapshot(
    RustGameProfileSnapshot Profile,
    RustGameBoosterAiAnalysisSnapshot? LastAnalysis);
