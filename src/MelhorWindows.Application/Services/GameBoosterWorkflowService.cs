using System.Globalization;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using MelhorWindows.Domain.Authorization;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace MelhorWindows.Application.Services;

[SupportedOSPlatform("windows")]
public sealed class GameBoosterWorkflowService(
    IAuthorizationService authorizationService,
    IRegistryEditingService registryEditingService,
    IRegistryInspectionService registryInspectionService,
    IRegistryAuditRepository registryAuditRepository,
    IProtectedStateStore protectedStateStore,
    IWindowsRestorePointService windowsRestorePointService)
{
    private static readonly IReadOnlyList<BoosterDefinition> Catalog =
    [
        new(
            "jb-gamebooster.game-mode",
            "Game Mode",
            "Sistema",
            "Mantem o Windows focado no jogo em execucao e reduz interferencias de segundo plano.",
            "Game Mode ativo.",
            "Game Mode ainda nao foi ativado.",
            "medio",
            "seguro",
            RequiresRestart: false,
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\GameBar",
                    "AutoGameModeEnabled",
                    1,
                    RegistryValueKind.DWord)
            ]),
        new(
            "jb-gamebooster.game-dvr",
            "Captura em segundo plano",
            "Captura",
            "Desativa gravacao e captura do Game DVR para liberar recursos e reduzir sobreposicoes.",
            "Captura e Game DVR desativados.",
            "Game DVR ainda esta ativo para o usuario atual.",
            "alto",
            "seguro",
            RequiresRestart: false,
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                    "AppCaptureEnabled",
                    0,
                    RegistryValueKind.DWord),
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"System\GameConfigStore",
                    "GameDVR_Enabled",
                    0,
                    RegistryValueKind.DWord)
            ]),
        new(
            "jb-gamebooster.system-responsiveness",
            "Responsividade do sistema",
            "CPU",
            "Prioriza workloads de jogo em vez de tarefas multimidia de fundo.",
            "SystemResponsiveness ajustado para gaming.",
            "SystemResponsiveness ainda esta acima do alvo para gaming.",
            "medio",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "SystemResponsiveness",
                    0,
                    RegistryValueKind.DWord)
            ]),
        new(
            "jb-gamebooster.network-throttling",
            "Network Throttling",
            "Rede",
            "Remove o throttling multimidia padrao do Windows para reduzir gargalos de rede em jogos online.",
            "Network Throttling desativado.",
            "Network Throttling ainda esta no modo padrao do Windows.",
            "alto",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                    "NetworkThrottlingIndex",
                    unchecked((int)0xffffffff),
                    RegistryValueKind.DWord)
            ]),
        new(
            "jb-gamebooster.foreground-priority",
            "Foreground Priority",
            "Memoria",
            "Ajusta a politica de prioridade do Windows para favorecer o app em foco.",
            "Win32PrioritySeparation configurado para foreground.",
            "Foreground Priority ainda nao foi ajustado.",
            "medio",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                    "Win32PrioritySeparation",
                    38,
                    RegistryValueKind.DWord)
            ])
    ];

    public async Task<GameBoosterDashboardSnapshot> GetDashboardSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var safetySettings = await LoadSafetySettingsAsync(cancellationToken);
        var lastSession = await protectedStateStore.LoadAsync<GameBoosterSessionSnapshot>(
            OptimizationStateKeys.LastGameBoosterSession,
            cancellationToken);

        var states = new List<GameBoosterOptimizationState>(Catalog.Count);

        foreach (var definition in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(await BuildStateAsync(definition, cancellationToken));
        }

        var optimizedCount = states.Count(item => item.IsOptimized);
        var score = Catalog.Count == 0
            ? 0
            : (int)Math.Round((double)optimizedCount / Catalog.Count * 100, MidpointRounding.AwayFromZero);

        return new GameBoosterDashboardSnapshot(
            BuildProfileName(optimizedCount),
            score,
            optimizedCount,
            Catalog.Count,
            safetySettings,
            lastSession?.AppliedAtUtc,
            lastSession?.RestorePointDescription,
            states);
    }

    public async Task<OptimizationSafetySettings> GetSafetySettingsAsync(CancellationToken cancellationToken = default)
    {
        return await LoadSafetySettingsAsync(cancellationToken);
    }

    public Task SaveSafetySettingsAsync(
        OptimizationSafetySettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return protectedStateStore.SaveAsync(OptimizationStateKeys.SafetySettings, settings, cancellationToken);
    }

    public async Task<OperationResult<GameBoosterDashboardSnapshot>> ApplyRecommendedAsync(
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var pendingDefinitions = new List<BoosterDefinition>(Catalog.Count);

        foreach (var definition in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await MatchesAsync(definition.ApplyChanges, cancellationToken))
            {
                pendingDefinitions.Add(definition);
            }
        }

        if (pendingDefinitions.Count == 0)
        {
            var snapshot = await GetDashboardSnapshotAsync(cancellationToken);
            return OperationResult<GameBoosterDashboardSnapshot>.Success(
                snapshot,
                "JB GameBooster ja esta alinhado com as recomendacoes iniciais.");
        }

        return await ApplyDefinitionsAsync(
            pendingDefinitions,
            "Otimizacoes recomendadas do JB GameBooster aplicadas.",
            cancellationToken);
    }

    public async Task<OperationResult<GameBoosterDashboardSnapshot>> ApplyOptimizationAsync(
        string optimizationId,
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var definition = Catalog.FirstOrDefault(item =>
            string.Equals(item.Id, optimizationId, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return OperationResult<GameBoosterDashboardSnapshot>.Failure("Otimizacao nao encontrada.");
        }

        if (await MatchesAsync(definition.ApplyChanges, cancellationToken))
        {
            var snapshot = await GetDashboardSnapshotAsync(cancellationToken);
            return OperationResult<GameBoosterDashboardSnapshot>.Success(
                snapshot,
                $"{definition.Title} ja esta alinhado.");
        }

        return await ApplyDefinitionsAsync(
            [definition],
            $"{definition.Title} aplicado pelo JB GameBooster.",
            cancellationToken);
    }

    public async Task<OperationResult<GameBoosterDashboardSnapshot>> RevertLastSessionAsync(
        CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.EditWindowsRegistry);

        var lastSession = await protectedStateStore.LoadAsync<GameBoosterSessionSnapshot>(
            OptimizationStateKeys.LastGameBoosterSession,
            cancellationToken);

        if (lastSession is null || lastSession.Optimizations.Count == 0)
        {
            return OperationResult<GameBoosterDashboardSnapshot>.Failure(
                "Nao existe sessao anterior do JB GameBooster para reverter.");
        }

        var revertChanges = lastSession.Optimizations
            .SelectMany(item => item.Values)
            .Select(snapshot => snapshot.ValueExisted
                ? new RegistryChangeRequest(
                    snapshot.Hive,
                    snapshot.KeyPath,
                    snapshot.ValueName,
                    snapshot.PreviousValue ?? 0,
                    RegistryValueKind.DWord)
                : new RegistryChangeRequest(
                    snapshot.Hive,
                    snapshot.KeyPath,
                    snapshot.ValueName,
                    0,
                    RegistryValueKind.DWord,
                    DeleteValue: true))
            .ToArray();

        var auditEntries = await registryEditingService.ApplyChangesAsync(revertChanges, cancellationToken);
        await registryAuditRepository.AddRangeAsync(auditEntries, cancellationToken);
        await protectedStateStore.DeleteAsync(OptimizationStateKeys.LastGameBoosterSession, cancellationToken);

        var dashboard = await GetDashboardSnapshotAsync(cancellationToken);
        return OperationResult<GameBoosterDashboardSnapshot>.Success(
            dashboard,
            "Ultima sessao do JB GameBooster revertida.");
    }

    private async Task<OperationResult<GameBoosterDashboardSnapshot>> ApplyDefinitionsAsync(
        IReadOnlyList<BoosterDefinition> definitions,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var safetySettings = await LoadSafetySettingsAsync(cancellationToken);
        string? restorePointDescription = null;

        if (safetySettings.CreateRestorePointBeforeApply)
        {
            restorePointDescription = BuildRestorePointDescription(definitions.Count);
            var restorePointResult = await windowsRestorePointService.CreateRestorePointAsync(
                restorePointDescription,
                cancellationToken);

            if (!restorePointResult.Succeeded)
            {
                return OperationResult<GameBoosterDashboardSnapshot>.Failure(
                    $"Nao foi possivel criar o ponto de restauracao: {restorePointResult.Message}");
            }
        }

        var sessionSnapshot = await CaptureSessionSnapshotAsync(
            definitions,
            restorePointDescription,
            cancellationToken);

        var changes = definitions
            .SelectMany(item => item.ApplyChanges)
            .ToArray();

        var auditEntries = await registryEditingService.ApplyChangesAsync(changes, cancellationToken);
        await registryAuditRepository.AddRangeAsync(auditEntries, cancellationToken);
        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.LastGameBoosterSession,
            sessionSnapshot,
            cancellationToken);

        var dashboard = await GetDashboardSnapshotAsync(cancellationToken);
        return OperationResult<GameBoosterDashboardSnapshot>.Success(dashboard, successMessage);
    }

    private async Task<GameBoosterSessionSnapshot> CaptureSessionSnapshotAsync(
        IReadOnlyList<BoosterDefinition> definitions,
        string? restorePointDescription,
        CancellationToken cancellationToken)
    {
        var optimizationSnapshots = new List<GameBoosterOptimizationSnapshot>(definitions.Count);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var valueSnapshots = new List<GameBoosterRegistryValueSnapshot>(definition.ApplyChanges.Count);

            foreach (var change in definition.ApplyChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentValue = await registryInspectionService.GetValueAsync(
                    change.Hive,
                    change.KeyPath,
                    change.ValueName,
                    cancellationToken);

                valueSnapshots.Add(new GameBoosterRegistryValueSnapshot(
                    change.Hive,
                    change.KeyPath,
                    change.ValueName,
                    ToNullableInt(currentValue),
                    currentValue is not null));
            }

            optimizationSnapshots.Add(new GameBoosterOptimizationSnapshot(
                definition.Id,
                definition.Title,
                valueSnapshots));
        }

        return new GameBoosterSessionSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            restorePointDescription,
            optimizationSnapshots);
    }

    private async Task<GameBoosterOptimizationState> BuildStateAsync(
        BoosterDefinition definition,
        CancellationToken cancellationToken)
    {
        var isOptimized = await MatchesAsync(definition.ApplyChanges, cancellationToken);

        return new GameBoosterOptimizationState(
            definition.Id,
            definition.Title,
            definition.Category,
            definition.Description,
            isOptimized ? definition.RecommendedLabel : definition.FallbackCurrentLabel,
            definition.RecommendedLabel,
            definition.ImpactLabel,
            definition.RiskLabel,
            isOptimized,
            definition.RequiresRestart);
    }

    private async Task<bool> MatchesAsync(
        IReadOnlyList<RegistryChangeRequest> expectedChanges,
        CancellationToken cancellationToken)
    {
        foreach (var expectedChange in expectedChanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentValue = await registryInspectionService.GetValueAsync(
                expectedChange.Hive,
                expectedChange.KeyPath,
                expectedChange.ValueName,
                cancellationToken);

            if (!RegistryValuesEqual(currentValue, expectedChange.Value))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<OptimizationSafetySettings> LoadSafetySettingsAsync(CancellationToken cancellationToken)
    {
        return await protectedStateStore.LoadAsync<OptimizationSafetySettings>(
                   OptimizationStateKeys.SafetySettings,
                   cancellationToken) ??
               OptimizationSafetySettings.Default;
    }

    private static string BuildRestorePointDescription(int itemCount)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"JB GameBooster - {itemCount} ajuste(s) - {timestamp}";
    }

    private static string BuildProfileName(int optimizedCount)
    {
        return optimizedCount switch
        {
            <= 0 => "Modo normal",
            >= 5 => "Performance maxima",
            >= 3 => "Balanceado",
            _ => "Custom"
        };
    }

    private static bool RegistryValuesEqual(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.Ordinal);
    }

    private static int? ToNullableInt(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private sealed record BoosterDefinition(
        string Id,
        string Title,
        string Category,
        string Description,
        string RecommendedLabel,
        string FallbackCurrentLabel,
        string ImpactLabel,
        string RiskLabel,
        bool RequiresRestart,
        IReadOnlyList<RegistryChangeRequest> ApplyChanges);
}
