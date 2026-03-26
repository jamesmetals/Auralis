using System.Text.Json;

namespace MelhorWindows.Infrastructure.AI;

internal static class GameOptimizationKnowledgeRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static GameOptimizationKnowledgePack? TryLoad(string gameKey)
    {
        if (string.IsNullOrWhiteSpace(gameKey))
        {
            return null;
        }

        var normalizedKey = gameKey.Trim().ToLowerInvariant();
        var resourceName = $"MelhorWindows.Infrastructure.KnowledgeBase.Games.{normalizedKey}.json";
        using var stream = typeof(GameOptimizationKnowledgeRepository).Assembly.GetManifestResourceStream(resourceName);

        return stream is null
            ? null
            : JsonSerializer.Deserialize<GameOptimizationKnowledgePack>(stream, SerializerOptions);
    }
}
