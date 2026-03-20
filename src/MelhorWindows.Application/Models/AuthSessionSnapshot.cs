namespace MelhorWindows.Application.Models;

public sealed record AuthSessionSnapshot(
    Guid UserId,
    string UserName,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);

