namespace MelhorWindows.Application.Models;

public sealed record LocalAiConnectionSettings(string EndpointUrl, string ModelName)
{
    public static LocalAiConnectionSettings Default { get; } = new("http://localhost:11434", "gemma3:4b");
}
