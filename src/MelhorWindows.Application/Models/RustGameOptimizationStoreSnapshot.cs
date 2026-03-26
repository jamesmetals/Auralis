namespace MelhorWindows.Application.Models;

public sealed record RustGameOptimizationUndoSnapshot(
    string OptimizationId,
    string Kind,
    string TargetPath,
    string TargetKey,
    string? PreviousValue,
    bool ValueExisted);

public sealed record RustGameOptimizationStoreSnapshot(
    DateTimeOffset? LastAppliedAtUtc,
    IReadOnlyList<RustGameOptimizationUndoSnapshot> AppliedOptimizations)
{
    public static RustGameOptimizationStoreSnapshot Empty { get; } =
        new(null, Array.Empty<RustGameOptimizationUndoSnapshot>());
}
