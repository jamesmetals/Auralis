namespace MelhorWindows.Application.Models;

public sealed record LocalAiConnectionSettings(string EndpointUrl, string ModelName)
{
    public static LocalAiConnectionSettings Default { get; } = new("ColeSuaChaveDaAPI_AIzaSy...", "gemini-2.5-flash");
}
