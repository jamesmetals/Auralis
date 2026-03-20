namespace MelhorWindows.Domain.Entities;

public sealed record AuthSession(
    Guid UserId,
    string UserName,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);

