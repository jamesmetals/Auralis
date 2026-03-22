using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using System.Runtime.Versioning;

namespace MelhorWindows.Application.Services;

[SupportedOSPlatform("windows")]
public sealed class GameBoosterAiWorkflowService(
    IProtectedStateStore protectedStateStore,
    ILocalAiGameBoosterService localAiGameBoosterService,
    GameBoosterWorkflowService gameBoosterWorkflowService,
    IRustGameProfileService rustGameProfileService)
{
    public async Task<GameBoosterAiPanelSnapshot> GetPanelSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var availability = await localAiGameBoosterService.GetAvailabilityAsync(settings, cancellationToken);
        var lastAnalysis = await protectedStateStore.LoadAsync<GameBoosterAiAnalysisSnapshot>(
            OptimizationStateKeys.LocalAiLastAnalysis,
            cancellationToken);

        return new GameBoosterAiPanelSnapshot(settings, availability, lastAnalysis);
    }

    public Task SaveSettingsAsync(
        LocalAiConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedSettings = new LocalAiConnectionSettings(
            string.IsNullOrWhiteSpace(settings.EndpointUrl)
                ? LocalAiConnectionSettings.Default.EndpointUrl
                : settings.EndpointUrl.Trim(),
            string.IsNullOrWhiteSpace(settings.ModelName)
                ? LocalAiConnectionSettings.Default.ModelName
                : settings.ModelName.Trim());

        return protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiSettings,
            normalizedSettings,
            cancellationToken);
    }

    public async Task<OperationResult<GameBoosterAiPanelSnapshot>> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var availability = await localAiGameBoosterService.GetAvailabilityAsync(settings, cancellationToken);

        if (!availability.IsReachable)
        {
            return OperationResult<GameBoosterAiPanelSnapshot>.Failure(availability.StatusMessage);
        }

        if (!availability.ConfiguredModelAvailable)
        {
            return OperationResult<GameBoosterAiPanelSnapshot>.Failure(
                $"O modelo configurado ({settings.ModelName}) nao apareceu no Ollama local. Rode `{availability.SuggestedPullCommand}` ou escolha um modelo listado.");
        }

        var dashboard = await gameBoosterWorkflowService.GetDashboardSnapshotAsync(cancellationToken);
        var analysis = await localAiGameBoosterService.AnalyzeGameBoosterAsync(settings, dashboard, cancellationToken);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiLastAnalysis,
            analysis,
            cancellationToken);

        var panel = new GameBoosterAiPanelSnapshot(settings, availability, analysis);
        return OperationResult<GameBoosterAiPanelSnapshot>.Success(panel, "Analise local concluida pelo JB GameBooster.");
    }

    public async Task<OperationResult<GameBoosterAiPanelSnapshot>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var panel = await GetPanelSnapshotAsync(cancellationToken);

        if (!panel.Availability.IsReachable)
        {
            return OperationResult<GameBoosterAiPanelSnapshot>.Failure(panel.Availability.StatusMessage);
        }

        if (!panel.Availability.ConfiguredModelAvailable)
        {
            return OperationResult<GameBoosterAiPanelSnapshot>.Failure(
                $"Conexao local ok, mas o modelo {panel.Settings.ModelName} ainda nao foi encontrado. Rode `{panel.Availability.SuggestedPullCommand}`.");
        }

        return OperationResult<GameBoosterAiPanelSnapshot>.Success(
            panel,
            $"Conexao local confirmada com Ollama e modelo {panel.Settings.ModelName} pronto para uso.");
    }

    public async Task<RustGameBoosterPanelSnapshot> GetRustPanelSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var profile = await rustGameProfileService.GetSnapshotAsync(cancellationToken);
        var lastAnalysis = await protectedStateStore.LoadAsync<RustGameBoosterAiAnalysisSnapshot>(
            OptimizationStateKeys.LocalAiRustLastAnalysis,
            cancellationToken);

        return new RustGameBoosterPanelSnapshot(profile, lastAnalysis);
    }

    public async Task<OperationResult<RustGameBoosterPanelSnapshot>> AnalyzeRustAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var availability = await localAiGameBoosterService.GetAvailabilityAsync(settings, cancellationToken);

        if (!availability.IsReachable)
        {
            return OperationResult<RustGameBoosterPanelSnapshot>.Failure(availability.StatusMessage);
        }

        if (!availability.ConfiguredModelAvailable)
        {
            return OperationResult<RustGameBoosterPanelSnapshot>.Failure(
                $"O modelo configurado ({settings.ModelName}) nao apareceu no Ollama local. Rode `{availability.SuggestedPullCommand}` ou escolha um modelo listado.");
        }

        var rustProfile = await rustGameProfileService.GetSnapshotAsync(cancellationToken);
        var boosterSnapshot = await gameBoosterWorkflowService.GetDashboardSnapshotAsync(cancellationToken);
        var analysis = await localAiGameBoosterService.AnalyzeRustProfileAsync(
            settings,
            rustProfile,
            boosterSnapshot,
            cancellationToken);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiRustLastAnalysis,
            analysis,
            cancellationToken);

        return OperationResult<RustGameBoosterPanelSnapshot>.Success(
            new RustGameBoosterPanelSnapshot(rustProfile, analysis),
            "Analise local do perfil de Rust concluida.");
    }

    private async Task<LocalAiConnectionSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        return await protectedStateStore.LoadAsync<LocalAiConnectionSettings>(
                   OptimizationStateKeys.LocalAiSettings,
                   cancellationToken) ??
               LocalAiConnectionSettings.Default;
    }

    private async Task<GameBoosterAiAnalysisSnapshot?> LoadLastAnalysisAsync(CancellationToken cancellationToken)
    {
        return await protectedStateStore.LoadAsync<GameBoosterAiAnalysisSnapshot>(
            OptimizationStateKeys.LocalAiLastAnalysis,
            cancellationToken);
    }
}
