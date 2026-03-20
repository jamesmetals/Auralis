using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Authorization;

namespace MelhorWindows.Infrastructure.Security;

public sealed class DevelopmentUserContext : IUserContext
{
    public DevelopmentUserContext()
    {
        var configuredRoles = Environment.GetEnvironmentVariable("MELHORWINDOWS_ACTIVE_ROLES");

        RoleNames = string.IsNullOrWhiteSpace(configuredRoles)
            ? [BuiltInRoles.Admin]
            : configuredRoles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public Guid UserId { get; } = Guid.Parse("3dd7f8ab-47d1-49f8-8adf-2747fd3270d4");

    public string UserName { get; } = Environment.UserName;

    public IReadOnlyCollection<string> RoleNames { get; }
}

