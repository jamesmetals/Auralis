using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;

namespace MelhorWindows.Infrastructure.AI;

public sealed class GoogleGeminiLocalAiGameBoosterService : ILocalAiGameBoosterService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<LocalAiAvailabilitySnapshot> GetAvailabilityAsync(LocalAiConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = ExtractApiKey(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
                return new LocalAiAvailabilitySnapshot(false, "A chave da API do Gemini e necessaria (insira no campo Endpoint).", Array.Empty<string>(), false, null);

            var model = string.IsNullOrWhiteSpace(settings.ModelName) ? "gemini-2.5-flash" : settings.ModelName;
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}?key={apiKey}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            var isSuccess = response.IsSuccessStatusCode;

            return new LocalAiAvailabilitySnapshot(
                isSuccess,
                isSuccess ? $"[{model}] Online" : "Chave da API invalida ou sem permissao.",
                new[] { model },
                isSuccess,
                null);
        }
        catch (Exception ex)
        {
            return new LocalAiAvailabilitySnapshot(false, ex.Message, Array.Empty<string>(), false, null);
        }
    }

    public async Task<GameBoosterAiAnalysisSnapshot> AnalyzeGameBoosterAsync(LocalAiConnectionSettings settings, GameBoosterDashboardSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var apiKey = ExtractApiKey(settings);
        var prompt = $"Como um especialista em otimizacao do Windows, analise o PC.\n" +
                     $"Score atual do GameBooster: {snapshot.OptimizationScore}%\n" +
                     $"Itens otimizados: {snapshot.OptimizedItemCount} de {snapshot.TotalItemCount}\n" +
                     "Responda EXATAMENTE neste formato JSON puro:\n" +
                     "{\n" +
                     "  \"Summary\": \"Um texto resumido de 1 paragrafo.\",\n" +
                     "  \"SuggestedProfile\": \"Performance Maxima\",\n" +
                     "  \"ReadinessLevel\": \"Alta\",\n" +
                     "  \"Recommendations\": [\n" +
                     "    { \"Priority\": \"Alta\", \"Title\": \"Desativar X\", \"Reason\": \"Causa lag\", \"SuggestedAction\": \"Desativar\", \"RelatedOptimizationId\": null }\n" +
                     "  ]\n" +
                     "}";

        var responseJson = await CallGeminiAsync(settings, prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
            return new GameBoosterAiAnalysisSnapshot(DateTimeOffset.UtcNow, apiKey, settings.ModelName, "Erro ao contatar Gemini", "N/A", "Desconhecido", Array.Empty<GameBoosterAiRecommendation>());

        try
        {
            responseJson = responseJson.Replace("```json", "").Replace("```", "").Trim();
            var doc = JsonNode.Parse(responseJson)!;

            var recArray = doc["Recommendations"]?.AsArray();
            var recs = new List<GameBoosterAiRecommendation>();
            if (recArray != null)
            {
                foreach(var item in recArray)
                {
                    recs.Add(new GameBoosterAiRecommendation(
                        item?["Priority"]?.GetValue<string>() ?? "Normal",
                        item?["Title"]?.GetValue<string>() ?? "Dica",
                        item?["Reason"]?.GetValue<string>() ?? "",
                        item?["SuggestedAction"]?.GetValue<string>() ?? "",
                        item?["RelatedOptimizationId"]?.GetValue<string>()));
                }
            }

            return new GameBoosterAiAnalysisSnapshot(
                DateTimeOffset.UtcNow,
                apiKey,
                settings.ModelName,
                doc["Summary"]?.GetValue<string>() ?? "Analise concluida.",
                doc["SuggestedProfile"]?.GetValue<string>() ?? "Normal",
                doc["ReadinessLevel"]?.GetValue<string>() ?? "Media",
                recs
            );
        }
        catch (Exception ex)
        {
            return new GameBoosterAiAnalysisSnapshot(DateTimeOffset.UtcNow, apiKey, settings.ModelName, "Falha de parse JSON: " + ex.Message, "Desconhecido", "Desconhecido", Array.Empty<GameBoosterAiRecommendation>());
        }
    }

    public async Task<RustGameBoosterAiAnalysisSnapshot> AnalyzeRustProfileAsync(LocalAiConnectionSettings settings, RustGameProfileSnapshot rustProfile, GameBoosterDashboardSnapshot boosterSnapshot, CancellationToken cancellationToken = default)
    {
        var apiKey = ExtractApiKey(settings);
        var prompt = $"Voce e um especialista em RUST e Windows. Avalie:\n" +
                     $"Args: {rustProfile.LaunchOptions}\nGC Buffer: {rustProfile.GcBufferCommand}\n" +
                     $"RAM: {rustProfile.TotalRamGb}GB\n" +
                     $"CPU Alvo: {rustProfile.CpuLabel}\n" +
                     $"Responda EXATAMENTE neste JSON puro:\n" +
                     "{\n" +
                     "  \"Summary\": \"Resumo da performance no Rust.\",\n" +
                     "  \"LaunchOptionsSummary\": \"O que achou dos argumentos.\",\n" +
                     "  \"Recommendations\": [\n" +
                     "    { \"Priority\": \"Alta\", \"Title\": \"Diminuir qualidade\", \"Reason\": \"RAM baixa\", \"SuggestedAction\": \"Mudar config\", \"RelatedOptimizationId\": null }\n" +
                     "  ]\n" +
                     "}";

        var responseJson = await CallGeminiAsync(settings, prompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
            return new RustGameBoosterAiAnalysisSnapshot(DateTimeOffset.UtcNow, apiKey, settings.ModelName, "Nenhum retorno do Gemini.", "N/A", Array.Empty<GameBoosterAiRecommendation>());

        try
        {
            responseJson = responseJson.Replace("```json", "").Replace("```", "").Trim();
            var doc = JsonNode.Parse(responseJson)!;

            var recArray = doc["Recommendations"]?.AsArray();
            var recs = new List<GameBoosterAiRecommendation>();
            if (recArray != null)
            {
                foreach(var item in recArray)
                {
                    recs.Add(new GameBoosterAiRecommendation(
                        item?["Priority"]?.GetValue<string>() ?? "Normal",
                        item?["Title"]?.GetValue<string>() ?? "Dica",
                        item?["Reason"]?.GetValue<string>() ?? "",
                        item?["SuggestedAction"]?.GetValue<string>() ?? "",
                        item?["RelatedOptimizationId"]?.GetValue<string>()));
                }
            }

            return new RustGameBoosterAiAnalysisSnapshot(
                DateTimeOffset.UtcNow,
                apiKey,
                settings.ModelName,
                doc["Summary"]?.GetValue<string>() ?? "Lido com sucesso",
                doc["LaunchOptionsSummary"]?.GetValue<string>() ?? "OK",
                recs
            );
        }
        catch (Exception ex)
        {
            return new RustGameBoosterAiAnalysisSnapshot(DateTimeOffset.UtcNow, apiKey, settings.ModelName, "Falha JSON: " + ex.Message, "Erro", Array.Empty<GameBoosterAiRecommendation>());
        }
    }

    public async Task<string> AnalyzeHardwareSnapshotAsync(LocalAiConnectionSettings settings, ComputerDiagnosticsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var prompt = "Analise esse hardware e faca uma recomendacao focada em FPS games:\n" +
                     $"CPU: {snapshot.CpuLabel} ({snapshot.LogicalCoreCount} Cores)\n" +
                     $"GPU: {snapshot.GpuLabel}\n" +
                     $"RAM: {snapshot.MemoryTotalGb} GB (Usando {snapshot.MemoryUsedGb} GB)\n" +
                     $"SO: {snapshot.WindowsVersion}\n" +
                     "Retorne um relatorio limpo em Portugues do Brasil (sem markdown JSON) apontando o principal gargalo.";

        var response = await CallGeminiAsync(settings, prompt, cancellationToken);
        return string.IsNullOrWhiteSpace(response) ? "Erro ao contatar API Google Gemini." : response;
    }

    private static string ExtractApiKey(LocalAiConnectionSettings settings)
    {
        if (settings.EndpointUrl.StartsWith("AIza", StringComparison.OrdinalIgnoreCase))
            return settings.EndpointUrl;

        if (settings.EndpointUrl.Contains("AIza"))
        {
            var idx = settings.EndpointUrl.IndexOf("AIza", StringComparison.OrdinalIgnoreCase);
            return settings.EndpointUrl.Substring(idx);
        }

        return settings.EndpointUrl;
    }

    private static async Task<string> CallGeminiAsync(LocalAiConnectionSettings settings, string systemPrompt, CancellationToken cancellationToken)
    {
        var apiKey = ExtractApiKey(settings);
        var model = string.IsNullOrWhiteSpace(settings.ModelName) ? "gemini-2.5-flash" : settings.ModelName;
        var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var payload = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = systemPrompt } } }
            },
            generationConfig = new
            {
                temperature = 0.2,
                topK = 40,
                topP = 0.95
            }
        };

        try 
        {
            var response = await _httpClient.PostAsJsonAsync(apiUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonParser = JsonNode.Parse(jsonStr);

            var text = jsonParser?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
            return text ?? string.Empty;
        } 
        catch 
        {
            return string.Empty;
        }
    }
}
