using System.Text;
using MelhorWindows.Application.Models;

namespace MelhorWindows.Infrastructure.AI;

internal static class GameOptimizationPromptContextBuilder
{
    public static string BuildRustKnowledgeContext(
        RustGameProfileSnapshot rustProfile,
        GameBoosterDashboardSnapshot boosterSnapshot)
    {
        var knowledgePack = GameOptimizationKnowledgeRepository.TryLoad("rust");
        if (knowledgePack is null)
        {
            return "Base pesquisada do Rust indisponivel. Analise apenas o snapshot local com cautela extra.";
        }

        var telemetry = boosterSnapshot.Telemetry;
        var applicableRules = new List<(GameOptimizationKnowledgeRule Rule, string MatchNote)>();
        var blockedRules = new List<(GameOptimizationKnowledgeRule Rule, string BlockReason)>();

        foreach (var rule in knowledgePack.RecommendationRules)
        {
            if (TryMatchRule(rule.Conditions, rustProfile, telemetry, out var matchNote, out var blockReason))
            {
                applicableRules.Add((rule, matchNote));
            }
            else
            {
                blockedRules.Add((rule, blockReason));
            }
        }

        var orderedApplicableRules = applicableRules
            .OrderByDescending(item => GetPriorityScore(item.Rule.Priority))
            .ThenBy(item => item.Rule.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var orderedBlockedRules = blockedRules
            .OrderByDescending(item => GetPriorityScore(item.Rule.Priority))
            .ThenBy(item => item.Rule.Title, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine($"Base de conhecimento persistida para {knowledgePack.GameTitle}.");
        builder.AppendLine($"Data da pesquisa consolidada: {knowledgePack.ResearchDate}.");
        builder.AppendLine($"Resumo pesquisado: {knowledgePack.ResearchSummary}");
        builder.AppendLine();
        builder.AppendLine("Fontes de maior peso:");

        foreach (var source in knowledgePack.Sources)
        {
            builder.AppendLine($"- [{source.EvidenceLevel}] {source.Label}: {source.Url}");
        }

        builder.AppendLine();
        builder.AppendLine("Principios fortes recorrentes:");
        foreach (var principle in knowledgePack.CorePrinciples)
        {
            builder.AppendLine($"- {principle}");
        }

        builder.AppendLine();
        builder.AppendLine("Mitos e atalhos para evitar:");
        foreach (var myth in knowledgePack.MythsToAvoid)
        {
            builder.AppendLine($"- {myth}");
        }

        builder.AppendLine();
        builder.AppendLine("Regras que combinam com o hardware atual:");
        foreach (var (rule, matchNote) in orderedApplicableRules)
        {
            builder.AppendLine($"- [{rule.Priority}] {rule.Title} ({rule.RuleType}, confianca {rule.Confidence})");
            builder.AppendLine($"  Recomendacao base: {rule.Recommendation}");
            builder.AppendLine($"  Motivo: {rule.Reason}");
            builder.AppendLine($"  Quando usar: {rule.Applicability}");
            builder.AppendLine($"  Match atual: {matchNote}");
        }

        if (orderedBlockedRules.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Regras fora do perfil atual ou de baixa prioridade para este hardware:");
            foreach (var (rule, blockReason) in orderedBlockedRules)
            {
                builder.AppendLine($"- {rule.Title}: {blockReason}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Instrucao para o analista:");
        builder.AppendLine("- Use a base pesquisada acima como referencia, mas decida apenas com base no snapshot recebido.");
        builder.AppendLine("- Sugira somente ajustes compativeis com CPU, RAM, GPU e fornecedor de driver detectados.");
        builder.AppendLine("- Diferencie ajuste imediato, teste reversivel e upgrade futuro.");
        builder.AppendLine("- Nao recomende placebo, launch option antiga sem contexto ou alteracao que contradiga a propria base pesquisada.");
        builder.AppendLine("- Se a melhoria depender de sintoma especifico, deixe isso explicito em vez de vender como ganho garantido.");

        return builder.ToString();
    }

    private static bool TryMatchRule(
        GameOptimizationKnowledgeConditions? conditions,
        RustGameProfileSnapshot rustProfile,
        GameBoosterTelemetrySnapshot telemetry,
        out string matchNote,
        out string blockReason)
    {
        var cpuLabel = telemetry.CpuLabel;
        var gpuVendor = DetectGpuVendor(telemetry.GpuLabel);
        var windowsVersion = telemetry.WindowsVersion;
        var notes = new List<string>();

        if (conditions is null)
        {
            matchNote = "Regra geral de comunidade/oficial; validar pelos sintomas e pelo servidor onde o usuario joga.";
            blockReason = string.Empty;
            return true;
        }

        if (conditions.MinRamGb is { } minRamGb && rustProfile.TotalRamGb < minRamGb)
        {
            matchNote = string.Empty;
            blockReason = $"RAM atual abaixo do patamar sugerido ({rustProfile.TotalRamGb} GB < {minRamGb} GB).";
            return false;
        }

        if (conditions.MaxRamGb is { } maxRamGb && rustProfile.TotalRamGb > maxRamGb)
        {
            matchNote = string.Empty;
            blockReason = $"RAM atual acima da faixa onde a regra costuma ter maior impacto ({rustProfile.TotalRamGb} GB > {maxRamGb} GB).";
            return false;
        }

        if (conditions.MinMemoryLoadPercent is { } minMemoryLoad && telemetry.CurrentMemoryLoadPercent < minMemoryLoad)
        {
            matchNote = string.Empty;
            blockReason = $"Carga de memoria atual nao esta alta o bastante para priorizar esta regra ({telemetry.CurrentMemoryLoadPercent}% < {minMemoryLoad}%).";
            return false;
        }

        if (conditions.MaxMemoryLoadPercent is { } maxMemoryLoad && telemetry.CurrentMemoryLoadPercent > maxMemoryLoad)
        {
            matchNote = string.Empty;
            blockReason = $"Carga de memoria atual excede a faixa prevista para esta regra ({telemetry.CurrentMemoryLoadPercent}% > {maxMemoryLoad}%).";
            return false;
        }

        if (conditions.CpuContainsAny is { Count: > 0 } cpuContainsAny &&
            !cpuContainsAny.Any(token => cpuLabel.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            matchNote = string.Empty;
            blockReason = "CPU atual nao bate com a familia alvo desta regra.";
            return false;
        }

        if (conditions.CpuExcludesAny is { Count: > 0 } cpuExcludesAny &&
            cpuExcludesAny.Any(token => cpuLabel.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            matchNote = string.Empty;
            blockReason = "A propria CPU atual entra na lista que deve evitar esta regra.";
            return false;
        }

        if (conditions.GpuVendorsAny is { Count: > 0 } gpuVendorsAny &&
            !gpuVendorsAny.Any(vendor => string.Equals(vendor, gpuVendor, StringComparison.OrdinalIgnoreCase)))
        {
            matchNote = string.Empty;
            blockReason = $"GPU atual nao pertence ao fornecedor esperado ({gpuVendor}).";
            return false;
        }

        if (conditions.WindowsContainsAny is { Count: > 0 } windowsContainsAny &&
            !windowsContainsAny.Any(token => windowsVersion.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            matchNote = string.Empty;
            blockReason = "Versao do Windows nao bate com o contexto previsto para esta regra.";
            return false;
        }

        if (conditions.MinRamGb is { } matchedMinRam)
        {
            notes.Add($"RAM atual atende o piso de {matchedMinRam} GB");
        }

        if (conditions.MaxRamGb is { } matchedMaxRam)
        {
            notes.Add($"RAM atual entra na faixa de maior impacto (<= {matchedMaxRam} GB)");
        }

        if (conditions.MinMemoryLoadPercent is { })
        {
            notes.Add($"carga de memoria atual esta em {telemetry.CurrentMemoryLoadPercent}%");
        }

        if (conditions.CpuContainsAny is { Count: > 0 })
        {
            notes.Add($"CPU atual corresponde ao filtro `{cpuLabel}`");
        }

        if (conditions.GpuVendorsAny is { Count: > 0 })
        {
            notes.Add($"GPU {telemetry.GpuLabel} foi classificada como {gpuVendor}");
        }

        if (notes.Count == 0)
        {
            notes.Add("regra geral aplicavel ao perfil atual");
        }

        matchNote = string.Join("; ", notes) + ".";
        blockReason = string.Empty;
        return true;
    }

    private static int GetPriorityScore(string priority)
    {
        return priority.ToLowerInvariant() switch
        {
            "alta" => 3,
            "media" => 2,
            "baixa" => 1,
            _ => 0
        };
    }

    private static string DetectGpuVendor(string gpuLabel)
    {
        if (gpuLabel.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
            gpuLabel.Contains("geforce", StringComparison.OrdinalIgnoreCase) ||
            gpuLabel.Contains("quadro", StringComparison.OrdinalIgnoreCase))
        {
            return "nvidia";
        }

        if (gpuLabel.Contains("amd", StringComparison.OrdinalIgnoreCase) ||
            gpuLabel.Contains("radeon", StringComparison.OrdinalIgnoreCase))
        {
            return "amd";
        }

        if (gpuLabel.Contains("intel", StringComparison.OrdinalIgnoreCase) ||
            gpuLabel.Contains("arc", StringComparison.OrdinalIgnoreCase))
        {
            return "intel";
        }

        return "desconhecido";
    }
}
