namespace MelhorWindows.Application.Models;

public sealed record RustGameProfileSnapshot(
    string CpuLabel,
    int TotalRamGb,
    string MemoryTierLabel,
    bool AvoidHighPriorityFlag,
    string LaunchOptions,
    string GcBufferCommand,
    string ClientConfigPath,
    bool ClientConfigDetected,
    string? SteamLocalConfigPath,
    bool SteamConfigDetected,
    string Summary,
    IReadOnlyList<string> RecommendedClientCommands,
    IReadOnlyList<string> OptionalClientCommands);
