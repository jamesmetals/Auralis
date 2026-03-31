using System.Text;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;

namespace MelhorWindows.Application.Services;

public sealed class RustGameOptimizationWorkflowService(
    IProtectedStateStore protectedStateStore,
    IRustGameProfileService rustGameProfileService)
{
    private const string RustAppId = "252490";

    public async Task<RustGameOptimizationCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var profile = await rustGameProfileService.GetSnapshotAsync(cancellationToken);
        var store = await LoadStoreAsync(cancellationToken);
        var optimizations = BuildOptimizationStates(profile, store);
        return new RustGameOptimizationCatalogSnapshot(profile, optimizations, store.LastAppliedAtUtc);
    }

    public async Task<OperationResult<RustGameOptimizationCatalogSnapshot>> ApplyRecommendedAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var pendingIds = snapshot.Optimizations.Where(item => item.CanApply).Select(item => item.Id).ToArray();

        if (pendingIds.Length == 0)
        {
            return OperationResult<RustGameOptimizationCatalogSnapshot>.Success(
                snapshot,
                "As otimizacoes automaticas do Rust ja estao alinhadas com o preset atual.");
        }

        var appliedCount = 0;

        foreach (var optimizationId in pendingIds)
        {
            var applyResult = await ApplyOptimizationInternalAsync(optimizationId, cancellationToken);
            if (!applyResult.Succeeded)
            {
                return applyResult;
            }

            appliedCount++;
        }

        var updatedSnapshot = await GetSnapshotAsync(cancellationToken);
        return OperationResult<RustGameOptimizationCatalogSnapshot>.Success(
            updatedSnapshot,
            $"{appliedCount} otimizacao(oes) automaticas do Rust foram aplicadas.");
    }

    public async Task<OperationResult<RustGameOptimizationCatalogSnapshot>> ApplyOptimizationAsync(
        string optimizationId,
        CancellationToken cancellationToken = default)
    {
        return await ApplyOptimizationInternalAsync(optimizationId, cancellationToken);
    }

    public async Task<OperationResult<RustGameOptimizationCatalogSnapshot>> RevertOptimizationAsync(
        string optimizationId,
        CancellationToken cancellationToken = default)
    {
        var store = await LoadStoreAsync(cancellationToken);
        var undoSnapshot = store.AppliedOptimizations.FirstOrDefault(item =>
            string.Equals(item.OptimizationId, optimizationId, StringComparison.OrdinalIgnoreCase));

        if (undoSnapshot is null)
        {
            return OperationResult<RustGameOptimizationCatalogSnapshot>.Failure(
                "Nao existe historico salvo para desfazer esta otimizacao do Rust.");
        }

        switch (undoSnapshot.Kind)
        {
            case "launch-flag":
                RestoreLaunchFlag(undoSnapshot);
                break;
            case "client-command":
                RestoreClientCommand(undoSnapshot);
                break;
            default:
                return OperationResult<RustGameOptimizationCatalogSnapshot>.Failure(
                    "Nao foi possivel identificar o tipo de otimizacao do Rust para desfazer.");
        }

        var remainingItems = store.AppliedOptimizations
            .Where(item => !string.Equals(item.OptimizationId, optimizationId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var updatedStore = remainingItems.Length == 0
            ? RustGameOptimizationStoreSnapshot.Empty
            : new RustGameOptimizationStoreSnapshot(store.LastAppliedAtUtc, remainingItems);

        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.RustOptimizationState,
            updatedStore,
            cancellationToken);

        var updatedSnapshotAfterUndo = await GetSnapshotAsync(cancellationToken);
        var optimizationTitle = updatedSnapshotAfterUndo.Optimizations
            .FirstOrDefault(item => string.Equals(item.Id, optimizationId, StringComparison.OrdinalIgnoreCase))?.Title
            ?? "Otimizacao";

        return OperationResult<RustGameOptimizationCatalogSnapshot>.Success(
            updatedSnapshotAfterUndo,
            $"{optimizationTitle} foi desfeita no Rust.");
    }

    private async Task<OperationResult<RustGameOptimizationCatalogSnapshot>> ApplyOptimizationInternalAsync(
        string optimizationId,
        CancellationToken cancellationToken)
    {
        var catalogSnapshot = await GetSnapshotAsync(cancellationToken);
        var optimization = catalogSnapshot.Optimizations.FirstOrDefault(item =>
            string.Equals(item.Id, optimizationId, StringComparison.OrdinalIgnoreCase));

        if (optimization is null)
        {
            return OperationResult<RustGameOptimizationCatalogSnapshot>.Failure("Otimizacao do Rust nao encontrada.");
        }

        if (!optimization.CanApply)
        {
            return OperationResult<RustGameOptimizationCatalogSnapshot>.Failure(
                $"{optimization.Title} nao pode ser aplicada no estado atual.");
        }

        var definition = BuildDefinitions(catalogSnapshot.Profile)
            .First(item => string.Equals(item.Id, optimizationId, StringComparison.OrdinalIgnoreCase));
        RustGameOptimizationUndoSnapshot undoSnapshot;

        switch (definition.Kind)
        {
            case RustOptimizationKind.LaunchFlag:
                undoSnapshot = ApplyLaunchFlag(catalogSnapshot.Profile, definition);
                break;
            case RustOptimizationKind.ClientCommand:
                undoSnapshot = ApplyClientCommand(catalogSnapshot.Profile, definition);
                break;
            case RustOptimizationKind.SystemSetting:
                undoSnapshot = ApplySystemSetting(definition);
                break;
            default:
                return OperationResult<RustGameOptimizationCatalogSnapshot>.Failure(
                    "Tipo de otimizacao do Rust nao suportado.");
        }

        var currentStore = await LoadStoreAsync(cancellationToken);
        var mergedItems = currentStore.AppliedOptimizations
            .Where(item => !string.Equals(item.OptimizationId, optimizationId, StringComparison.OrdinalIgnoreCase))
            .Append(undoSnapshot)
            .ToArray();

        var updatedStore = new RustGameOptimizationStoreSnapshot(DateTimeOffset.UtcNow, mergedItems);
        await protectedStateStore.SaveAsync(
            OptimizationStateKeys.RustOptimizationState,
            updatedStore,
            cancellationToken);

        var updatedSnapshot = await GetSnapshotAsync(cancellationToken);
        return OperationResult<RustGameOptimizationCatalogSnapshot>.Success(
            updatedSnapshot,
            $"{optimization.Title} aplicada no Rust.");
    }

    private static IReadOnlyList<RustGameOptimizationState> BuildOptimizationStates(
        RustGameProfileSnapshot profile,
        RustGameOptimizationStoreSnapshot store)
    {
        var definitions = BuildDefinitions(profile);
        var states = new List<RustGameOptimizationState>(definitions.Count);

        foreach (var definition in definitions)
        {
            states.Add(BuildOptimizationState(profile, store, definition));
        }

        return states;
    }

    private static RustGameOptimizationState BuildOptimizationState(
        RustGameProfileSnapshot profile,
        RustGameOptimizationStoreSnapshot store,
        RustOptimizationDefinition definition)
    {
        var canUndo = store.AppliedOptimizations.Any(item =>
            string.Equals(item.OptimizationId, definition.Id, StringComparison.OrdinalIgnoreCase));

        return definition.Kind switch
        {
            RustOptimizationKind.LaunchFlag => BuildLaunchFlagState(profile, definition, canUndo),
            RustOptimizationKind.ClientCommand => BuildClientCommandState(profile, definition, canUndo),
            RustOptimizationKind.SystemSetting => BuildSystemSettingState(definition, canUndo),
            _ => new RustGameOptimizationState(
                definition.Id,
                definition.Title,
                definition.Category,
                definition.Description,
                "Tipo desconhecido",
                "Nao avaliado.",
                definition.RecommendedText,
                false,
                false,
                canUndo,
                "Aplicar",
                "Desfazer")
        };
    }

    private static RustGameOptimizationState BuildLaunchFlagState(
        RustGameProfileSnapshot profile,
        RustOptimizationDefinition definition,
        bool canUndo)
    {
        if (!profile.SteamConfigDetected || string.IsNullOrWhiteSpace(profile.SteamLocalConfigPath))
        {
            return new RustGameOptimizationState(
                definition.Id,
                definition.Title,
                definition.Category,
                definition.Description,
                "Steam localconfig.vdf",
                "Steam localconfig.vdf nao foi encontrado neste perfil.",
                definition.RecommendedText,
                false,
                false,
                canUndo,
                "Aplicar",
                "Desfazer");
        }

        var launchRead = TryReadRustLaunchOptions(profile.SteamLocalConfigPath);
        if (!launchRead.BlockFound)
        {
            return new RustGameOptimizationState(
                definition.Id,
                definition.Title,
                definition.Category,
                definition.Description,
                "Steam localconfig.vdf",
                "A entrada do Rust nao foi encontrada no localconfig do Steam.",
                definition.RecommendedText,
                false,
                false,
                canUndo,
                "Aplicar",
                "Desfazer");
        }

        var isApplied = ParseLaunchOptionTokens(launchRead.LaunchOptions)
            .Contains(definition.TargetKey, StringComparer.OrdinalIgnoreCase);

        var currentText = string.IsNullOrWhiteSpace(launchRead.LaunchOptions)
            ? "Nenhuma launch option registrada hoje para o Rust."
            : $"Atual no Steam: {launchRead.LaunchOptions}";

        return new RustGameOptimizationState(
            definition.Id,
            definition.Title,
            definition.Category,
            definition.Description,
            "Steam > LaunchOptions do Rust",
            currentText,
            definition.RecommendedText,
            isApplied,
            !isApplied,
            canUndo,
            isApplied ? "Ja aplicado" : "Aplicar",
            "Desfazer");
    }

    private static RustGameOptimizationState BuildClientCommandState(
        RustGameProfileSnapshot profile,
        RustOptimizationDefinition definition,
        bool canUndo)
    {
        var currentValue = TryReadClientCommandValue(profile.ClientConfigPath, definition.TargetKey, out var valueExists);
        var isApplied = valueExists &&
                        string.Equals(currentValue, definition.TargetValue, StringComparison.OrdinalIgnoreCase);

        var currentText = valueExists
            ? $"Valor atual em client.cfg: {definition.TargetKey} {currentValue}"
            : $"Comando ainda nao encontrado em {profile.ClientConfigPath}";

        return new RustGameOptimizationState(
            definition.Id,
            definition.Title,
            definition.Category,
            definition.Description,
            "Rust client.cfg",
            currentText,
            definition.RecommendedText,
            isApplied,
            !isApplied,
            canUndo,
            isApplied ? "Ja aplicado" : "Aplicar",
            "Desfazer");
    }

    private static RustGameOptimizationState BuildSystemSettingState(
        RustOptimizationDefinition definition,
        bool canUndo)
    {
        var isApplied = false;
        var currentText = "Verificando configuracao do sistema...";

        if (definition.TargetKey == "pagefile")
        {
            currentText = "Pagefile: verificar em Configuracoes do Sistema > Desempenho > Avancado";
        }

        return new RustGameOptimizationState(
            definition.Id,
            definition.Title,
            definition.Category,
            definition.Description,
            "Sistema",
            currentText,
            definition.RecommendedText,
            isApplied,
            !isApplied,
            canUndo,
            "Aplicar",
            "Desfazer");
    }

    private static RustGameOptimizationUndoSnapshot ApplySystemSetting(RustOptimizationDefinition definition)
    {
        if (definition.TargetKey == "pagefile")
        {
            return new RustGameOptimizationUndoSnapshot(
                definition.Id,
                "system-setting",
                "pagefile",
                null,
                "auto",
                false);
        }

        return new RustGameOptimizationUndoSnapshot(
            definition.Id,
            "system-setting",
            definition.TargetKey,
            null,
            definition.TargetValue,
            false);
    }

    private static List<RustOptimizationDefinition> BuildDefinitions(RustGameProfileSnapshot profile)
    {
        var definitions = new List<RustOptimizationDefinition>
        {
            new(
                "rust-launch-nolog",
                "Adicionar -nolog no Steam",
                "Launch options",
                "Reduz ruido de log no launch do Rust sem mexer em qualidade grafica.",
                RustOptimizationKind.LaunchFlag,
                "-nolog",
                string.Empty,
                "Steam vai passar a abrir Rust com `-nolog`."),
            new(
                "rust-launch-no-browser",
                "Adicionar -no-browser no Steam",
                "Launch options",
                "Evita carregar o browser interno no launch do Rust, reduzindo ruido no arranque.",
                RustOptimizationKind.LaunchFlag,
                "-no-browser",
                string.Empty,
                "Steam vai passar a abrir Rust com `-no-browser`."),
            new(
                "rust-client-gc-buffer",
                "Aplicar gc.buffer recomendado",
                "client.cfg",
                "Ajusta o `gc.buffer` de acordo com a memoria detectada no PC.",
                RustOptimizationKind.ClientCommand,
                "gc.buffer",
                ExtractCommandValue(profile.GcBufferCommand),
                $"client.cfg vai receber `{profile.GcBufferCommand}`."),
            new(
                "rust-client-occlusion",
                "Garantir occlusion true",
                "client.cfg",
                "Mantem o comando de occlusion ativo no client.cfg do Rust.",
                RustOptimizationKind.ClientCommand,
                "occlusion",
                "true",
                "client.cfg vai receber `occlusion true`."),
            new(
                "rust-client-graphics-damage",
                "Aplicar graphics.damage false",
                "client.cfg",
                "Desliga o feedback visual de dano para reduzir ruido visual e manter o preset alinhado.",
                RustOptimizationKind.ClientCommand,
                "graphics.damage",
                "false",
                "client.cfg vai receber `graphics.damage false`."),
            new(
                "rust-client-graphics-branding",
                "Aplicar graphics.branding false",
                "client.cfg",
                "Desliga branding grafico adicional no preset automatico do Rust.",
                RustOptimizationKind.ClientCommand,
                "graphics.branding",
                "false",
                "client.cfg vai receber `graphics.branding false`."),

            // === Otimizacoes baseadas na IA consultiva do Rust ===
            new(
                "rust-system-pagefile-auto",
                "Pagefile automatico",
                "Sistema",
                "Garante que o Windows esteja gerenciando automaticamente o pagefile em um SSD/NVMe com espaco suficiente.",
                RustOptimizationKind.SystemSetting,
                "pagefile",
                "auto",
                "Pagefile configurado para gerenciamento automatico."),
            new(
                "rust-client-max-tick-rate",
                "Aplicar maxTickRate 128",
                "client.cfg",
                "Limita tick rate do servidor para 128, reduzindo carga no cliente em servidores lotados.",
                RustOptimizationKind.ClientCommand,
                "maxTickRate",
                "128",
                "client.cfg vai receber `maxTickRate 128`."),
            new(
                "rust-client-shadows-disabled",
                "Desativar sombras",
                "client.cfg",
                "Desabilita sombras para melhorar FPS em hardware modesto.",
                RustOptimizationKind.ClientCommand,
                "graphics.shadows",
                "false",
                "client.cfg vai receber `graphics.shadows false`."),
            new(
                "rust-client-sun-shadows",
                "Desativar sun shadows",
                "client.cfg",
                "Desabilita sombras do sol para melhorar performance.",
                RustOptimizationKind.ClientCommand,
                "graphics.sun shafts",
                "false",
                "client.cfg vai接收 `graphics.sun shafts false`.")
        };

        if (profile.TotalRamGb <= 8)
        {
            definitions.Add(
                new(
                    "rust-client-grass-on",
                    "Aplicar grass.on false",
                    "client.cfg",
                    "Em maquinas muito apertadas de memoria, corta gramado para aliviar o preset base.",
                    RustOptimizationKind.ClientCommand,
                    "grass.on",
                    "false",
                    "client.cfg vai receber `grass.on false`."));
        }

        return definitions;
    }

    private RustGameOptimizationUndoSnapshot ApplyLaunchFlag(
        RustGameProfileSnapshot profile,
        RustOptimizationDefinition definition)
    {
        var path = profile.SteamLocalConfigPath ??
                   throw new InvalidOperationException("Steam localconfig.vdf nao encontrado para aplicar launch options.");
        var launchRead = TryReadRustLaunchOptions(path);

        if (!launchRead.BlockFound)
        {
            throw new InvalidOperationException("A entrada do Rust nao foi encontrada no localconfig do Steam.");
        }

        var tokens = ParseLaunchOptionTokens(launchRead.LaunchOptions);
        var previousFlagExists = tokens.Contains(definition.TargetKey, StringComparer.OrdinalIgnoreCase);

        if (!previousFlagExists)
        {
            tokens.Add(definition.TargetKey);
            WriteRustLaunchOptions(path, string.Join(" ", tokens));
        }

        return new RustGameOptimizationUndoSnapshot(
            definition.Id,
            "launch-flag",
            path,
            definition.TargetKey,
            null,
            previousFlagExists);
    }

    private static void RestoreLaunchFlag(RustGameOptimizationUndoSnapshot undoSnapshot)
    {
        var launchRead = TryReadRustLaunchOptions(undoSnapshot.TargetPath);
        if (!launchRead.BlockFound)
        {
            throw new InvalidOperationException("A entrada do Rust nao foi encontrada para desfazer a launch option.");
        }

        var tokens = ParseLaunchOptionTokens(launchRead.LaunchOptions);

        if (undoSnapshot.ValueExisted)
        {
            if (!tokens.Contains(undoSnapshot.TargetKey, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(undoSnapshot.TargetKey);
            }
        }
        else
        {
            tokens = tokens
                .Where(item => !string.Equals(item, undoSnapshot.TargetKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        WriteRustLaunchOptions(undoSnapshot.TargetPath, string.Join(" ", tokens));
    }

    private static RustGameOptimizationUndoSnapshot ApplyClientCommand(
        RustGameProfileSnapshot profile,
        RustOptimizationDefinition definition)
    {
        var currentValue = TryReadClientCommandValue(profile.ClientConfigPath, definition.TargetKey, out var valueExists);
        WriteClientCommandValue(profile.ClientConfigPath, definition.TargetKey, definition.TargetValue);

        return new RustGameOptimizationUndoSnapshot(
            definition.Id,
            "client-command",
            profile.ClientConfigPath,
            definition.TargetKey,
            currentValue,
            valueExists);
    }

    private static void RestoreClientCommand(RustGameOptimizationUndoSnapshot undoSnapshot)
    {
        if (undoSnapshot.ValueExisted)
        {
            WriteClientCommandValue(
                undoSnapshot.TargetPath,
                undoSnapshot.TargetKey,
                undoSnapshot.PreviousValue ?? string.Empty);
        }
        else
        {
            RemoveClientCommandValue(undoSnapshot.TargetPath, undoSnapshot.TargetKey);
        }
    }

    private async Task<RustGameOptimizationStoreSnapshot> LoadStoreAsync(CancellationToken cancellationToken)
    {
        return await protectedStateStore.LoadAsync<RustGameOptimizationStoreSnapshot>(
                   OptimizationStateKeys.RustOptimizationState,
                   cancellationToken) ??
               RustGameOptimizationStoreSnapshot.Empty;
    }

    private static string ExtractCommandValue(string command)
    {
        var separatorIndex = command.IndexOf(' ');
        return separatorIndex < 0
            ? string.Empty
            : command[(separatorIndex + 1)..].Trim();
    }

    private static string? TryReadClientCommandValue(string path, string key, out bool valueExists)
    {
        valueExists = false;

        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            valueExists = true;
            if (trimmed.Length == key.Length)
            {
                return string.Empty;
            }

            return trimmed[(key.Length + 1)..].Trim();
        }

        return null;
    }

    private static void WriteClientCommandValue(string path, string key, string value)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var lines = File.Exists(fullPath)
            ? File.ReadAllLines(fullPath).ToList()
            : new List<string>();
        var replacementLine = $"{key} {value}".TrimEnd();
        var replaced = false;

        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = replacementLine;
                replaced = true;
            }
        }

        if (!replaced)
        {
            lines.Add(replacementLine);
        }

        File.WriteAllText(fullPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
    }

    private static void RemoveClientCommandValue(string path, string key)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var lines = File.ReadAllLines(path)
            .Where(line =>
            {
                var trimmed = line.Trim();
                return !trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
    }

    private static RustLaunchOptionsReadResult TryReadRustLaunchOptions(string path)
    {
        if (!File.Exists(path))
        {
            return new RustLaunchOptionsReadResult(false, null, null, null, null);
        }

        var content = File.ReadAllText(path, Encoding.UTF8);
        var appIndex = content.IndexOf($"\"{RustAppId}\"", StringComparison.OrdinalIgnoreCase);
        if (appIndex < 0)
        {
            return new RustLaunchOptionsReadResult(false, null, null, null, null);
        }

        var blockStart = content.IndexOf('{', appIndex);
        if (blockStart < 0)
        {
            return new RustLaunchOptionsReadResult(false, null, null, null, null);
        }

        var blockEnd = FindMatchingBrace(content, blockStart);
        if (blockEnd < 0)
        {
            return new RustLaunchOptionsReadResult(false, null, null, null, null);
        }

        var blockContent = content.Substring(blockStart, blockEnd - blockStart + 1);
        var propertyMatch = System.Text.RegularExpressions.Regex.Match(
            blockContent,
            "\"LaunchOptions\"\\s*\"(?<value>[^\"]*)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return new RustLaunchOptionsReadResult(
            true,
            blockStart,
            blockEnd,
            propertyMatch.Success ? propertyMatch.Groups["value"].Value : string.Empty,
            content);
    }

    private static void WriteRustLaunchOptions(string path, string launchOptions)
    {
        var readResult = TryReadRustLaunchOptions(path);
        if (!readResult.BlockFound || readResult.BlockStart is null || readResult.BlockEnd is null || readResult.Content is null)
        {
            throw new InvalidOperationException("Nao foi possivel localizar a entrada do Rust no localconfig do Steam.");
        }

        var content = readResult.Content;
        var blockStart = readResult.BlockStart.Value;
        var blockEnd = readResult.BlockEnd.Value;
        var blockContent = content.Substring(blockStart, blockEnd - blockStart + 1);
        var normalizedValue = launchOptions.Trim();
        var escapedValue = normalizedValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string updatedBlock;

        var propertyMatch = System.Text.RegularExpressions.Regex.Match(
            blockContent,
            "\"LaunchOptions\"\\s*\"(?<value>[^\"]*)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (propertyMatch.Success)
        {
            updatedBlock = System.Text.RegularExpressions.Regex.Replace(
                blockContent,
                "\"LaunchOptions\"\\s*\"[^\"]*\"",
                $"\"LaunchOptions\"\t\t\"{escapedValue}\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        else
        {
            var insertion = $"{newline}\t\t\t\t\"LaunchOptions\"\t\t\"{escapedValue}\"";
            updatedBlock = blockContent.Insert(blockContent.Length - 1, insertion);
        }

        var updatedContent = content[..blockStart] + updatedBlock + content[(blockEnd + 1)..];
        File.WriteAllText(path, updatedContent, Encoding.UTF8);
    }

    private static List<string> ParseLaunchOptionTokens(string? launchOptions)
    {
        if (string.IsNullOrWhiteSpace(launchOptions))
        {
            return [];
        }

        return launchOptions
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int FindMatchingBrace(string content, int openBraceIndex)
    {
        var depth = 0;

        for (var index = openBraceIndex; index < content.Length; index++)
        {
            if (content[index] == '{')
            {
                depth++;
            }
            else if (content[index] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private enum RustOptimizationKind
    {
        LaunchFlag,
        ClientCommand,
        SystemSetting
    }

    private sealed record RustOptimizationDefinition(
        string Id,
        string Title,
        string Category,
        string Description,
        RustOptimizationKind Kind,
        string TargetKey,
        string TargetValue,
        string RecommendedText);

    private sealed record RustLaunchOptionsReadResult(
        bool BlockFound,
        int? BlockStart,
        int? BlockEnd,
        string? LaunchOptions,
        string? Content);
}
