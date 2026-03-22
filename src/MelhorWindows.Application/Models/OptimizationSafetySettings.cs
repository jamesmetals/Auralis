namespace MelhorWindows.Application.Models;

public sealed record OptimizationSafetySettings(bool CreateRestorePointBeforeApply)
{
    public static OptimizationSafetySettings Default { get; } = new(CreateRestorePointBeforeApply: true);
}
