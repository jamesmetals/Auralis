using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using System.Runtime.Versioning;

namespace MelhorWindows.Application.Services;

[SupportedOSPlatform("windows")]
public sealed class GameBoosterAiWorkflowService(
    IProtectedStateStore protectedStateStore,
    ILocalAiGameBoosterService localAiGameBoosterService,
    GameBoosterWorkflowService gameBoosterWorkflowService,
    RustGameOptimizationWorkflowService rustGameOptimizationWorkflowService)
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
                BuildModelUnavailableMessage(settings.ModelName, availability));
        }

        var dashboard = await gameBoosterWorkflowService.GetDashboardSnapshotAsync(cancellationToken);
        var analysis = await localAiGameBoosterService.AnalyzeGameBoosterAsync(settings, dashboard, cancellationToken);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiLastAnalysis,
            analysis,
            cancellationToken);

        var panel = new GameBoosterAiPanelSnapshot(settings, availability, analysis);
        return OperationResult<GameBoosterAiPanelSnapshot>.Success(panel, "Analise concluida pelo JB GameBooster.");
    }

    /// <summary>
    /// Analisa o sistema com a IA e aplica automaticamente todas as otimizações pendentes do catálogo.
    /// </summary>
    public async Task<OperationResult<GameBoosterAiPanelSnapshot>> AnalyzeAndApplyAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken);
        var availability = await localAiGameBoosterService.GetAvailabilityAsync(settings, cancellationToken);

        if (!availability.IsReachable)
            return OperationResult<GameBoosterAiPanelSnapshot>.Failure(availability.StatusMessage);

        // 1. Obter diagnóstico da IA
        var dashboard = await gameBoosterWorkflowService.GetDashboardSnapshotAsync(cancellationToken);
        var analysis = await localAiGameBoosterService.AnalyzeGameBoosterAsync(settings, dashboard, cancellationToken);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiLastAnalysis,
            analysis,
            cancellationToken);

        // 2. Aplicar automaticamente todas as otimizações pendentes do catálogo
        var applyResult = await gameBoosterWorkflowService.ApplyRecommendedAsync(cancellationToken);

        // 3. Retornar painel atualizado com resultado
        var updatedDashboard = applyResult.Value ?? dashboard;
        var panel = new GameBoosterAiPanelSnapshot(settings, availability, analysis);

        var message = applyResult.Succeeded
            ? $"IA analisou o PC e aplicou {updatedDashboard.OptimizedItemCount} otimizacoes no sistema."
            : $"IA analisou o PC, mas houve um erro na aplicacao: {applyResult.Message}";

        return applyResult.Succeeded
            ? OperationResult<GameBoosterAiPanelSnapshot>.Success(panel, message)
            : OperationResult<GameBoosterAiPanelSnapshot>.Failure(message);
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
                BuildModelUnavailableMessage(panel.Settings.ModelName, panel.Availability));
        }

        return OperationResult<GameBoosterAiPanelSnapshot>.Success(
            panel,
            $"Conexao confirmada com Google Gemini e modelo {panel.Settings.ModelName} pronto para uso.");
    }

    public async Task<RustGameBoosterPanelSnapshot> GetRustPanelSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var rustCatalog = await rustGameOptimizationWorkflowService.GetSnapshotAsync(cancellationToken);
        var lastAnalysis = await protectedStateStore.LoadAsync<RustGameBoosterAiAnalysisSnapshot>(
            OptimizationStateKeys.LocalAiRustLastAnalysis,
            cancellationToken);

        return new RustGameBoosterPanelSnapshot(
            rustCatalog.Profile,
            rustCatalog.Optimizations,
            rustCatalog.LastAppliedAtUtc,
            lastAnalysis);
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
                BuildModelUnavailableMessage(settings.ModelName, availability));
        }

        var rustCatalog = await rustGameOptimizationWorkflowService.GetSnapshotAsync(cancellationToken);
        var boosterSnapshot = await gameBoosterWorkflowService.GetDashboardSnapshotAsync(cancellationToken);
        var analysis = await localAiGameBoosterService.AnalyzeRustProfileAsync(
            settings,
            rustCatalog.Profile,
            boosterSnapshot,
            cancellationToken);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LocalAiRustLastAnalysis,
            analysis,
            cancellationToken);

        var refreshedRustCatalog = await rustGameOptimizationWorkflowService.GetSnapshotAsync(cancellationToken);
        return OperationResult<RustGameBoosterPanelSnapshot>.Success(
            new RustGameBoosterPanelSnapshot(
                refreshedRustCatalog.Profile,
                refreshedRustCatalog.Optimizations,
                refreshedRustCatalog.LastAppliedAtUtc,
                analysis),
            "Leitura consultiva do Rust concluida. Nenhuma alteracao foi aplicada automaticamente.");
    }

    public async Task<OperationResult<RustGameBoosterPanelSnapshot>> ApplyRustOptimizationAsync(
        string optimizationId,
        CancellationToken cancellationToken = default)
    {
        var result = await rustGameOptimizationWorkflowService.ApplyOptimizationAsync(optimizationId, cancellationToken);
        return await BuildRustOperationResultAsync(result, cancellationToken);
    }

    public async Task<OperationResult<RustGameBoosterPanelSnapshot>> ApplyAllRustOptimizationsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await rustGameOptimizationWorkflowService.ApplyRecommendedAsync(cancellationToken);
        return await BuildRustOperationResultAsync(result, cancellationToken);
    }

    public async Task<OperationResult<RustGameBoosterPanelSnapshot>> RevertRustOptimizationAsync(
        string optimizationId,
        CancellationToken cancellationToken = default)
    {
        var result = await rustGameOptimizationWorkflowService.RevertOptimizationAsync(optimizationId, cancellationToken);
        return await BuildRustOperationResultAsync(result, cancellationToken);
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

    private static string BuildModelUnavailableMessage(
        string modelName,
        LocalAiAvailabilitySnapshot availability)
    {
        return string.IsNullOrWhiteSpace(availability.SuggestedPullCommand)
            ? $"O modelo configurado ({modelName}) nao esta disponivel para a chave atual do Gemini. Escolha um dos modelos listados."
            : $"O modelo configurado ({modelName}) nao esta disponivel. Rode `{availability.SuggestedPullCommand}` ou escolha um modelo listado.";
    }

    private async Task<OperationResult<RustGameBoosterPanelSnapshot>> BuildRustOperationResultAsync(
        OperationResult<RustGameOptimizationCatalogSnapshot> result,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded || result.Value is null)
        {
            return OperationResult<RustGameBoosterPanelSnapshot>.Failure(result.Message);
        }

        var lastAnalysis = await protectedStateStore.LoadAsync<RustGameBoosterAiAnalysisSnapshot>(
            OptimizationStateKeys.LocalAiRustLastAnalysis,
            cancellationToken);

        return OperationResult<RustGameBoosterPanelSnapshot>.Success(
            new RustGameBoosterPanelSnapshot(
                result.Value.Profile,
                result.Value.Optimizations,
                result.Value.LastAppliedAtUtc,
                lastAnalysis),
            result.Message);
    }
}
