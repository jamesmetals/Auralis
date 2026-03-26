namespace MelhorWindows.Application.Models;

public sealed record RustGameOptimizationState(
    string Id,
    string Title,
    string Category,
    string Description,
    string TargetText,
    string CurrentText,
    string RecommendedText,
    bool IsApplied,
    bool CanApply,
    bool CanUndo,
    string ApplyButtonText,
    string UndoButtonText);
