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
    IWindowsRestorePointService windowsRestorePointService,
    IComputerDiagnosticsService computerDiagnosticsService)
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
            ]),

        // ── Novas otimizacoes baseadas em pesquisa tecnica (2024-2025) ──

        new(
            "jb-gamebooster.hags",
            "Hardware Accelerated GPU Scheduling",
            "GPU",
            "Transfere o agendamento de frames da CPU para a GPU, reduzindo latencia de entrada em GPUs modernas (RTX 3000+, RX 6000+). Requer driver atualizado e reinicio.",
            "HAGS ativo — GPU gerencia o proprio agendamento de frames.",
            "HAGS inativo — CPU ainda gerencia o agendamento de frames da GPU.",
            "alto",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                    "HwSchMode",
                    2,
                    RegistryValueKind.DWord)
            ]),

        new(
            "jb-gamebooster.mmcss-games",
            "Prioridade MMCSS para Jogos",
            "CPU",
            "Configura o agendador multimidia do Windows (MMCSS) para dar maxima prioridade de CPU e GPU a processos de jogo. Algumas instalacoes do Windows podem ter esses valores alterados por updates.",
            "MMCSS configurado: GPU Priority 8, CPU Priority 6, Scheduling High.",
            "MMCSS ainda nao esta com prioridade maxima para jogos.",
            "medio",
            "seguro",
            RequiresRestart: false,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "GPU Priority",
                    8,
                    RegistryValueKind.DWord),
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Priority",
                    6,
                    RegistryValueKind.DWord),
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "Scheduling Category",
                    "High",
                    RegistryValueKind.String),
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    "SFIO Priority",
                    "High",
                    RegistryValueKind.String)
            ]),

        new(
            "jb-gamebooster.svchost-split",
            "Consolidacao de Processos do Sistema",
            "Memoria",
            "Agrupa servicos do Windows em menos processos svchost, reduzindo context switches e consumo de RAM. Recomendado apenas em sistemas com 16 GB+ de RAM. Requer reinicio.",
            "Threshold de split configurado para sistemas de alta memoria.",
            "svchost ainda opera com isolamento padrao por servico.",
            "baixo",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control",
                    "SvcHostSplitThresholdInKB",
                    16777216,
                    RegistryValueKind.DWord)
            ]),

        // ── Otimizacoes de PC Avançadas (Plano Auralis) ──

        new(
            "jb-gamebooster.telemetry",
            "Privacidade e Telemetria",
            "Sistema",
            "Desativa a coleta de dados em segundo plano (Telemetria) do Windows, poupando ciclos de CPU e disco.",
            "Telemetria principal desativada.",
            "Coleta de dados do Windows ativa.",
            "medio",
            "seguro",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    "AllowTelemetry",
                    0,
                    RegistryValueKind.DWord)
            ]),

        new(
            "jb-gamebooster.power-throttling",
            "Desativar Power Throttling",
            "Energia",
            "Impede que o Windows reduza a energia de processos em segundo plano ou jogos, garantindo foco no maximo desempenho termico.",
            "Power Throttling desativado sistematicamente.",
            "Gerenciamento de energia dinamico limitando processos.",
            "alto",
            "moderado",
            RequiresRestart: true,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
                    "PowerThrottlingOff",
                    1,
                    RegistryValueKind.DWord)
            ]),

        new(
            "jb-gamebooster.ultimate-performance",
            "Ativar Plano Ultimate Performance",
            "Energia",
            "Ativa o plano de energia Ultimate Performance do Windows para maximo desempenho.",
            "Plano Ultimate Performance ativado.",
            "Plano de energia padrao ativo.",
            "alto",
            "seguro",
            RequiresRestart: false,
            [
                new RegistryChangeRequest(
                    RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes",
                    "ActivePowerScheme",
                    "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                    RegistryValueKind.String)
            ]),

        new(
            "jb-gamebooster.nvidia-power-management",
            "NVIDIA Preferir Desempenho Maximo",
            "GPU",
            "Forca a placa de video NVIDIA a manter clocks altos. Abra o Painel de Controle NVIDIA manualmente.",
            "NVIDIA configurado para Preferir desempenho maximo.",
            "NVIDIA em modo adaptativo.",
            "medio",
            "seguro",
            RequiresRestart: false,
            []),
        new(
            "jb-gamebooster.visual-effects",
            "Ajustar Efeitos Visuais",
            "Sistema",
            "Configura o Windows para priorizar desempenho sobre efeitos visuais, removendo animacoes e transicoes desnecessarias.",
            "Efeitos visuais ajustados para melhor desempenho.",
            "Efeitos visuais completos ativos.",
            "baixo",
            "seguro",
            RequiresRestart: false,
            [
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Control Panel\Desktop",
                    "MenuShowDelay",
                    0,
                    RegistryValueKind.String),
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Control Panel\Desktop",
                    "ForegroundLockTimeout",
                    0,
                    RegistryValueKind.DWord),
                new RegistryChangeRequest(
                    RegistryHive.CurrentUser,
                    @"Control Panel\Desktop",
                    "VisualFXSetting",
                    2,
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
        var computerSnapshot = await computerDiagnosticsService.GetSnapshotAsync(cancellationToken);
        var telemetry = BuildTelemetrySnapshot(states, score, computerSnapshot);

        return new GameBoosterDashboardSnapshot(
            BuildProfileName(optimizedCount),
            score,
            optimizedCount,
            Catalog.Count,
            safetySettings,
            lastSession?.AppliedAtUtc,
            lastSession?.RestorePointDescription,
            states,
            telemetry);
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

    public async Task<IReadOnlyList<GameBoosterOptimizationState>> GetPendingOptimizationsAsync(
        CancellationToken cancellationToken = default)
    {
        var pending = new List<GameBoosterOptimizationState>();
        foreach (var definition in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await MatchesAsync(definition.ApplyChanges, cancellationToken))
            {
                pending.Add(new GameBoosterOptimizationState(
                    definition.Id,
                    definition.Title,
                    definition.Category,
                    definition.Description,
                    string.Empty,
                    string.Empty,
                    definition.ImpactLabel,
                    definition.RiskLabel,
                    false,
                    definition.RequiresRestart));
            }
        }
        return pending;
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
            >= 7 => "Performance maxima",
            >= 4 => "Balanceado",
            _ => "Custom"
        };
    }

    private static GameBoosterTelemetrySnapshot BuildTelemetrySnapshot(
        IReadOnlyList<GameBoosterOptimizationState> states,
        int optimizationScore,
        ComputerDiagnosticsSnapshot computerSnapshot)
    {
        var appliedStates = states.Where(state => state.IsOptimized).ToArray();
        var pendingStates = states.Where(state => !state.IsOptimized).ToArray();

        var currentEstimatedFps = EstimateCurrentFps(appliedStates, computerSnapshot);
        var projectedFpsGain = (int)Math.Round(
            pendingStates.Sum(GetFpsGainWeight),
            MidpointRounding.AwayFromZero);
        var projectedEstimatedFps = Math.Clamp(currentEstimatedFps + projectedFpsGain, currentEstimatedFps, 240);

        var cpuReliefPercent = Math.Round(
            Math.Min(computerSnapshot.CpuUsagePercent - 2d, pendingStates.Sum(GetCpuReliefWeight)),
            1,
            MidpointRounding.AwayFromZero);
        cpuReliefPercent = Math.Max(0, cpuReliefPercent);
        var estimatedCpuAfterPercent = Math.Round(
            Math.Max(2d, computerSnapshot.CpuUsagePercent - cpuReliefPercent),
            1,
            MidpointRounding.AwayFromZero);

        var memorySavingsGb = Math.Round(
            Math.Min(Math.Max(0, computerSnapshot.MemoryUsedGb - 1d), pendingStates.Sum(GetMemorySavingsWeight)),
            1,
            MidpointRounding.AwayFromZero);
        memorySavingsGb = Math.Max(0, memorySavingsGb);
        var estimatedMemoryUsedAfterGb = Math.Round(
            Math.Max(0.8d, computerSnapshot.MemoryUsedGb - memorySavingsGb),
            1,
            MidpointRounding.AwayFromZero);
        var estimatedMemoryLoadAfterPercent = computerSnapshot.MemoryTotalGb <= 0
            ? 0
            : (int)Math.Round(
                Math.Clamp(estimatedMemoryUsedAfterGb / computerSnapshot.MemoryTotalGb * 100d, 0d, 100d),
                MidpointRounding.AwayFromZero);

        var restartPendingCount = pendingStates.Count(state => state.RequiresRestart);
        var scanHighlights = new List<string>
        {
            $"CPU detectada: {computerSnapshot.CpuLabel} ({computerSnapshot.LogicalCoreCount} threads lógicas).",
            $"GPU detectada: {computerSnapshot.GpuLabel}.",
            $"Memoria em uso agora: {computerSnapshot.MemoryUsedGb:0.0} GB de {computerSnapshot.MemoryTotalGb:0.0} GB.",
            $"Estado atual do booster: {appliedStates.Length} de {states.Count} ajustes alinhados.",
            restartPendingCount == 0
                ? "Nenhum ajuste pendente exige reinicio no momento."
                : $"{restartPendingCount} ajuste(s) pendente(s) ainda devem pedir reinicio para entregar tudo."
        };

        return new GameBoosterTelemetrySnapshot(
            optimizationScore,
            NormalizeFpsForGauge(projectedEstimatedFps),
            (int)Math.Round(Math.Clamp(computerSnapshot.CpuUsagePercent, 0d, 100d), MidpointRounding.AwayFromZero),
            (int)Math.Round(Math.Clamp(computerSnapshot.MemoryLoadPercent, 0d, 100d), MidpointRounding.AwayFromZero),
            currentEstimatedFps,
            projectedEstimatedFps,
            projectedEstimatedFps - currentEstimatedFps,
            computerSnapshot.CpuUsagePercent,
            estimatedCpuAfterPercent,
            cpuReliefPercent,
            computerSnapshot.MemoryUsedGb,
            computerSnapshot.MemoryTotalGb,
            estimatedMemoryUsedAfterGb,
            memorySavingsGb,
            (int)Math.Round(Math.Clamp(computerSnapshot.MemoryLoadPercent, 0d, 100d), MidpointRounding.AwayFromZero),
            estimatedMemoryLoadAfterPercent,
            computerSnapshot.GpuLabel,
            computerSnapshot.CpuLabel,
            computerSnapshot.LogicalCoreCount,
            computerSnapshot.WindowsVersion,
            $"{appliedStates.Length} de {states.Count} otimizacoes ja estao aplicadas neste perfil.",
            projectedEstimatedFps == currentEstimatedFps
                ? $"Sem ganho extra relevante estimado agora. Perfil atual: {currentEstimatedFps} FPS estimados."
                : $"Estimativa heuristica: {currentEstimatedFps} FPS agora, com meta de {projectedEstimatedFps} FPS apos concluir os pendentes.",
            cpuReliefPercent <= 0
                ? $"Uso atual da CPU em torno de {computerSnapshot.CpuUsagePercent:0.0}%."
                : $"Uso atual da CPU em torno de {computerSnapshot.CpuUsagePercent:0.0}%, com alivio estimado de {cpuReliefPercent:0.0} ponto(s).",
            memorySavingsGb <= 0
                ? $"RAM atual: {computerSnapshot.MemoryUsedGb:0.0} / {computerSnapshot.MemoryTotalGb:0.0} GB."
                : $"RAM atual: {computerSnapshot.MemoryUsedGb:0.0} / {computerSnapshot.MemoryTotalGb:0.0} GB, com economia estimada de {memorySavingsGb:0.0} GB.",
            scanHighlights);
    }

    private static int EstimateCurrentFps(
        IReadOnlyList<GameBoosterOptimizationState> appliedStates,
        ComputerDiagnosticsSnapshot computerSnapshot)
    {
        var baseFps = 72;
        var cpuLabel = computerSnapshot.CpuLabel;

        if (computerSnapshot.MemoryTotalGb >= 32)
        {
            baseFps += 14;
        }
        else if (computerSnapshot.MemoryTotalGb >= 16)
        {
            baseFps += 8;
        }
        else if (computerSnapshot.MemoryTotalGb >= 8)
        {
            baseFps += 3;
        }

        baseFps += computerSnapshot.LogicalCoreCount switch
        {
            >= 24 => 12,
            >= 16 => 9,
            >= 12 => 6,
            >= 8 => 4,
            _ => 0
        };

        if (cpuLabel.Contains("X3D", StringComparison.OrdinalIgnoreCase))
        {
            baseFps += 20;
        }
        else if (cpuLabel.Contains("Ryzen 9", StringComparison.OrdinalIgnoreCase) ||
                 cpuLabel.Contains("i9", StringComparison.OrdinalIgnoreCase))
        {
            baseFps += 14;
        }
        else if (cpuLabel.Contains("Ryzen 7", StringComparison.OrdinalIgnoreCase) ||
                 cpuLabel.Contains("i7", StringComparison.OrdinalIgnoreCase))
        {
            baseFps += 10;
        }
        else if (cpuLabel.Contains("Ryzen 5", StringComparison.OrdinalIgnoreCase) ||
                 cpuLabel.Contains("i5", StringComparison.OrdinalIgnoreCase))
        {
            baseFps += 6;
        }

        var appliedGain = appliedStates.Sum(GetFpsGainWeight);
        var loadPenalty = computerSnapshot.CpuUsagePercent * 0.35d +
                          Math.Max(0d, computerSnapshot.MemoryLoadPercent - 62d) * 0.25d;

        return Math.Clamp(
            (int)Math.Round(baseFps + appliedGain - loadPenalty, MidpointRounding.AwayFromZero),
            35,
            240);
    }

    private static double GetFpsGainWeight(GameBoosterOptimizationState state)
    {
        var impactWeight = GetImpactWeight(state.ImpactLabel);
        var categoryWeight = state.Category switch
        {
            "GPU" => 1.45,
            "Captura" => 1.30,
            "CPU" => 1.20,
            "Sistema" => 1.05,
            "Memoria" => 0.95,
            "Rede" => 0.75,
            _ => 1.00
        };

        return impactWeight * categoryWeight + (state.RequiresRestart ? 0.4 : 0d);
    }

    private static double GetCpuReliefWeight(GameBoosterOptimizationState state)
    {
        var impactWeight = state.ImpactLabel switch
        {
            "alto" => 4.2,
            "medio" => 2.4,
            "baixo" => 1.1,
            _ => 1.5
        };

        var categoryWeight = state.Category switch
        {
            "CPU" => 1.20,
            "Sistema" => 1.00,
            "Captura" => 0.90,
            "GPU" => 0.80,
            "Memoria" => 0.45,
            _ => 0.35
        };

        return impactWeight * categoryWeight;
    }

    private static double GetMemorySavingsWeight(GameBoosterOptimizationState state)
    {
        var impactWeight = state.ImpactLabel switch
        {
            "alto" => 0.35,
            "medio" => 0.18,
            "baixo" => 0.08,
            _ => 0.12
        };

        var categoryWeight = state.Category switch
        {
            "Memoria" => 1.35,
            "Captura" => 1.10,
            "Sistema" => 0.70,
            "CPU" => 0.45,
            _ => 0.30
        };

        return impactWeight * categoryWeight;
    }

    private static double GetImpactWeight(string impactLabel)
    {
        return impactLabel switch
        {
            "alto" => 4.4,
            "medio" => 2.6,
            "baixo" => 1.2,
            _ => 1.8
        };
    }

    private static int NormalizeFpsForGauge(int fps)
    {
        return (int)Math.Round(Math.Clamp(fps / 240d * 100d, 0d, 100d), MidpointRounding.AwayFromZero);
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
