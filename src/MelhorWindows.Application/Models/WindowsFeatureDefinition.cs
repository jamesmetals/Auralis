namespace MelhorWindows.Application.Models;

public sealed record WindowsFeatureDefinition(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<RegistryChangeRequest> EnableChanges,
    IReadOnlyList<RegistryChangeRequest> DisableChanges);

