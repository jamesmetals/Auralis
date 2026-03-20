namespace MelhorWindows.Domain.Entities;

public sealed record LicenseEntitlement(
    Guid LicenseId,
    string PlanCode,
    string DeviceId,
    bool IsActive,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset LastValidatedAtUtc);

