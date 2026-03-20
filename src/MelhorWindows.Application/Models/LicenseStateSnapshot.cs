namespace MelhorWindows.Application.Models;

public sealed record LicenseStateSnapshot(
    Guid LicenseId,
    string PlanCode,
    string DeviceId,
    bool IsActive,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset LastValidatedAtUtc);
