using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;

namespace MelhorWindows.Infrastructure.AI;

public sealed class OllamaLocalAiGameBoosterService : ILocalAiGameBoosterService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly object AnalysisSchema = new
    {
        type = "object",
        properties = new
        {
            executiveSummary = new { type = "string" },
            recommendedProfile = new { type = "string" },
            readinessLevel = new { type = "string" },
            recommendations = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        priority = new { type = "string" },
                        title = new { type = "string" },
                        reason = new { type = "string" },
                        suggestedAction = new { type = "string" },
                        relatedOptimizationId = new { type = new[] { "string", "null" } }
                    },
                    required = new[] { "priority", "title", "reason", "suggestedAction", "relatedOptimizationId" }
                }
            }
        },
        required = new[] { "executiveSummary", "recommendedProfile", "readinessLevel", "recommendations" }
    };

    private static readonly object RustAnalysisSchema = new
    {
        type = "object",
        properties = new
        {
            executiveSummary = new { type = "string" },
            launchOptionsSummary = new { type = "string" },
            recommendations = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        priority = new { type = "string" },
                        title = new { type = "string" },
                        reason = new { type = "string" },
                        suggestedAction = new { type = "string" },
                        relatedOptimizationId = new { type = new[] { "string", "null" } }
                    },
                    required = new[] { "priority", "title", "reason", "suggestedAction", "relatedOptimizationId" }
                }
            }
        },
        required = new[] { "executiveSummary", "launchOptionsSummary", "recommendations" }
    };

    public async Task<LocalAiAvailabilitySnapshot> GetAvailabilityAsync(
        LocalAiConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateClient(settings.EndpointUrl, timeout: TimeSpan.FromSeconds(3));
            using var response = await client.GetAsync("api/tags", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new LocalAiAvailabilitySnapshot(
                    false,
                    $"Ollama respondeu com status {(int)response.StatusCode} ao listar modelos locais.",
                    [],
                    false,
                    BuildPullCommand(settings.ModelName));
            }

            var payload = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(SerializerOptions, cancellationToken);
            var models = payload?.Models?
                .Select(model => model.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];

            var message = models.Length == 0
                ? "Ollama esta acessivel, mas ainda nao ha modelos baixados localmente."
                : models.Contains(settings.ModelName, StringComparer.OrdinalIgnoreCase)
                    ? $"Ollama acessivel com {models.Length} modelo(s) local(is), incluindo {settings.ModelName}."
                    : $"Ollama acessivel com {models.Length} modelo(s) local(is), mas o modelo configurado ainda nao esta presente.";

            var modelAvailable = models.Contains(settings.ModelName, StringComparer.OrdinalIgnoreCase);

            return new LocalAiAvailabilitySnapshot(
                true,
                message,
                models,
                modelAvailable,
                modelAvailable ? null : BuildPullCommand(settings.ModelName));
        }
        catch (HttpRequestException)
        {
            return new LocalAiAvailabilitySnapshot(
                false,
                $"Nao foi possivel alcancar o Ollama em {settings.EndpointUrl}. Verifique se o app/daemon esta aberto.",
                [],
                false,
                BuildPullCommand(settings.ModelName));
        }
        catch (TaskCanceledException)
        {
            return new LocalAiAvailabilitySnapshot(
                false,
                "A conexao com o Ollama expirou antes da resposta.",
                [],
                false,
                BuildPullCommand(settings.ModelName));
        }
    }

    public async Task<GameBoosterAiAnalysisSnapshot> AnalyzeGameBoosterAsync(
        LocalAiConnectionSettings settings,
        GameBoosterDashboardSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(settings.EndpointUrl, timeout: TimeSpan.FromSeconds(75));

        var request = new
        {
            model = settings.ModelName,
            stream = false,
            format = AnalysisSchema,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce eh um analista local do JB GameBooster. Responda em portugues do Brasil. Use apenas os dados recebidos. Foque em seguranca, custo zero, clareza e acoes praticas. Nao invente hardware nem benchmark."
                },
                new
                {
                    role = "user",
                    content = BuildAnalysisPrompt(snapshot)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("api/chat", request, SerializerOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama nao concluiu a analise local. Status {(int)response.StatusCode}: {responseText}");
        }

        var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, SerializerOptions)
            ?? throw new InvalidOperationException("Resposta vazia do Ollama.");

        if (string.IsNullOrWhiteSpace(chatResponse.Message?.Content))
        {
            throw new InvalidOperationException("Ollama respondeu sem conteudo para a analise.");
        }

        var analysis = JsonSerializer.Deserialize<OllamaAnalysisPayload>(
                           chatResponse.Message.Content,
                           SerializerOptions) ??
                       throw new InvalidOperationException("Nao foi possivel interpretar o JSON da analise local.");

        return new GameBoosterAiAnalysisSnapshot(
            DateTimeOffset.UtcNow,
            settings.EndpointUrl,
            settings.ModelName,
            analysis.ExecutiveSummary,
            analysis.RecommendedProfile,
            analysis.ReadinessLevel,
            analysis.Recommendations
                .Select(item => new GameBoosterAiRecommendation(
                    item.Priority,
                    item.Title,
                    item.Reason,
                    item.SuggestedAction,
                    item.RelatedOptimizationId))
                .ToArray());
    }

    public async Task<RustGameBoosterAiAnalysisSnapshot> AnalyzeRustProfileAsync(
        LocalAiConnectionSettings settings,
        RustGameProfileSnapshot rustProfile,
        GameBoosterDashboardSnapshot boosterSnapshot,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(settings.EndpointUrl, timeout: TimeSpan.FromSeconds(75));

        var request = new
        {
            model = settings.ModelName,
            stream = false,
            format = RustAnalysisSchema,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce eh um analista local especializado em Rust para o modulo JB GameBooster. Responda em portugues do Brasil, sem inventar hardware. Priorize estabilidade, frametime e ajustes reversiveis. Quando citar launch options, seja conservador."
                },
                new
                {
                    role = "user",
                    content = BuildRustAnalysisPrompt(rustProfile, boosterSnapshot)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("api/chat", request, SerializerOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama nao concluiu a analise local do Rust. Status {(int)response.StatusCode}: {responseText}");
        }

        var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, SerializerOptions)
            ?? throw new InvalidOperationException("Resposta vazia do Ollama.");

        if (string.IsNullOrWhiteSpace(chatResponse.Message?.Content))
        {
            throw new InvalidOperationException("Ollama respondeu sem conteudo para a analise de Rust.");
        }

        var analysis = JsonSerializer.Deserialize<OllamaRustAnalysisPayload>(
                           chatResponse.Message.Content,
                           SerializerOptions) ??
                       throw new InvalidOperationException("Nao foi possivel interpretar o JSON da analise local de Rust.");

        return new RustGameBoosterAiAnalysisSnapshot(
            DateTimeOffset.UtcNow,
            settings.EndpointUrl,
            settings.ModelName,
            analysis.ExecutiveSummary,
            analysis.LaunchOptionsSummary,
            analysis.Recommendations
                .Select(item => new GameBoosterAiRecommendation(
                    item.Priority,
                    item.Title,
                    item.Reason,
                    item.SuggestedAction,
                    item.RelatedOptimizationId))
                .ToArray());
    }

    public async Task<string> AnalyzeHardwareSnapshotAsync(
        LocalAiConnectionSettings settings,
        ComputerDiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(settings.EndpointUrl, timeout: TimeSpan.FromSeconds(12));

        var request = new
        {
            model = settings.ModelName,
            stream = false,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Voce eh um analista experiente em otimizacao e resolucao de problemas de performance do Windows. Escreva um relatorio detalhado, tecnico mas facil de ler. Destaque viloes de memoria, gargalos de CPU/GPU e de sugestoes praticas do que o usuario pode fechar ou configurar no Windows para melhorar FPS nos games. Formate a saida em Markdown elegante com icones e tabelas se necessario. Fale OBRIGATORIAMENTE em portugues do Brasil."
                },
                new
                {
                    role = "user",
                    content = BuildHardwareSnapshotPrompt(snapshot)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("api/chat", request, SerializerOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Ollama falhou ao gerar o diagnostico de hardware. Status {(int)response.StatusCode}: {responseText}");
        }

        var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, SerializerOptions)
            ?? throw new InvalidOperationException("Resposta vazia do Ollama.");

        return chatResponse.Message?.Content ?? "O modelo de IA nao retornou nenhum conteudo valido.";
    }

    private static HttpClient CreateClient(string endpointUrl, TimeSpan? timeout = null)
    {
        var normalizedBaseUrl = endpointUrl.Trim().TrimEnd('/') + "/";
        var client = new HttpClient
        {
            BaseAddress = new Uri(normalizedBaseUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(45)
        };

        return client;
    }

    private static string BuildAnalysisPrompt(GameBoosterDashboardSnapshot snapshot)
    {
        var serializedSnapshot = JsonSerializer.Serialize(snapshot, SerializerOptions);

        return
            """
            Analise o snapshot atual do modulo JB GameBooster.

            Regras:
            - Responda estritamente no JSON pedido pelo schema.
            - Priorize recomendacoes seguras e de baixo atrito.
            - Se o score ja estiver alto, foque em validacao, reboot pendente e ajustes futuros.
            - Referencie optimizationId apenas quando existir item claramente relacionado.
            - Mantenha no maximo 4 recomendacoes.

            Snapshot:
            """ + Environment.NewLine + serializedSnapshot;
    }

    private static string BuildRustAnalysisPrompt(
        RustGameProfileSnapshot rustProfile,
        GameBoosterDashboardSnapshot boosterSnapshot)
    {
        var serializedRustProfile = JsonSerializer.Serialize(rustProfile, SerializerOptions);
        var serializedBooster = JsonSerializer.Serialize(boosterSnapshot, SerializerOptions);
        var knowledgeContext = GameOptimizationPromptContextBuilder.BuildRustKnowledgeContext(rustProfile, boosterSnapshot);

        return
            """
            Analise o perfil local de Rust e o estado geral do JB GameBooster usando a base pesquisada persistida.

            Regras:
            - Responda estritamente no JSON pedido pelo schema.
            - Nao invente paths, GPU, monitor ou benchmark.
            - Use o launch options ja calculado como ponto de partida, mas critique e reduza flags antigas se a base pesquisada apontar risco/placebo.
            - Sugira somente ajustes compativeis com CPU, RAM, GPU e vendor detectados.
            - Diferencie ajuste imediato, teste reversivel e upgrade futuro.
            - Se detectar RAM baixa, memoria pressionada, CPU X3D ou conflito de vendor, destaque isso.
            - Mantenha no maximo 4 recomendacoes.

            Base pesquisada persistida:
            """ + Environment.NewLine + knowledgeContext + Environment.NewLine + Environment.NewLine +
            """

            Perfil Rust:
            """ + Environment.NewLine + serializedRustProfile + Environment.NewLine + Environment.NewLine +
            """
            Estado geral do booster:
            """ + Environment.NewLine + serializedBooster;
    }

    private static string BuildHardwareSnapshotPrompt(ComputerDiagnosticsSnapshot snapshot)
    {
        var topProcessesList = string.Join(Environment.NewLine, snapshot.TopMemoryProcesses.Select(p => $"- {p.ProcessName}: {p.MemoryUsedGb} GB"));

        return
            $"""
             Analise este retrato instantaneo do computador do usuario e indique recomendacoes ou possiveis dores de cabeca que esses componentes podem causar.
             Foque tambem na relacao entre CPU e GPU, e analise a lista de processos que mais estao consumindo RAM no background.
             
             OS: {snapshot.WindowsVersion}
             CPU: {snapshot.CpuLabel} ({snapshot.LogicalCoreCount} cores) - Uso atual: {snapshot.CpuUsagePercent}%
             GPU: {snapshot.GpuLabel}
             Memoria RAM Total: {snapshot.MemoryTotalGb} GB (Carga do Windows: {snapshot.MemoryLoadPercent}%)
             Memoria Usada: {snapshot.MemoryUsedGb} GB / Disp: {snapshot.MemoryAvailableGb} GB
             
             Top processos mais pesados no instante da foto:
             {topProcessesList}
             """;
    }

    private static string BuildPullCommand(string modelName)
    {
        return $"ollama pull {modelName}";
    }

    private sealed record OllamaTagsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<OllamaModelInfo>? Models);

    private sealed record OllamaModelInfo(
        [property: JsonPropertyName("name")] string Name);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage? Message);

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaAnalysisPayload(
        string ExecutiveSummary,
        string RecommendedProfile,
        string ReadinessLevel,
        IReadOnlyList<OllamaRecommendationPayload> Recommendations);

    private sealed record OllamaRustAnalysisPayload(
        string ExecutiveSummary,
        string LaunchOptionsSummary,
        IReadOnlyList<OllamaRecommendationPayload> Recommendations);

    private sealed record OllamaRecommendationPayload(
        string Priority,
        string Title,
        string Reason,
        string SuggestedAction,
        string? RelatedOptimizationId);
}
