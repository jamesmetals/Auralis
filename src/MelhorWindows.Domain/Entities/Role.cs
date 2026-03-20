namespace MelhorWindows.Domain.Entities;

public sealed record Role(
    Guid Id,
    string Name,
    IReadOnlyCollection<string> Permissions);

