using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Abstractions;

public interface ILocalAiGameBoosterService
{
    Task<LocalAiAvailabilitySnapshot> GetAvailabilityAsync(
        LocalAiConnectionSettings settings,
        CancellationToken cancellationToken = default);

    Task<GameBoosterAiAnalysisSnapshot> AnalyzeGameBoosterAsync(
        LocalAiConnectionSettings settings,
        GameBoosterDashboardSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<RustGameBoosterAiAnalysisSnapshot> AnalyzeRustProfileAsync(
        LocalAiConnectionSettings settings,
        RustGameProfileSnapshot rustProfile,
        GameBoosterDashboardSnapshot boosterSnapshot,
        CancellationToken cancellationToken = default);

    Task<string> AnalyzeHardwareSnapshotAsync(
        LocalAiConnectionSettings settings,
        ComputerDiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
