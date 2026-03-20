namespace MelhorWindows.Domain.Entities;

public sealed record User(
    Guid Id,
    string UserName,
    IReadOnlyCollection<string> RoleNames);

