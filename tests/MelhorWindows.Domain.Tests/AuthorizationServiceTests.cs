using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Services;
using MelhorWindows.Domain.Authorization;
using Xunit;

namespace MelhorWindows.Domain.Tests;

public sealed class AuthorizationServiceTests
{
    [Fact]
    public void AdminRole_HasAccessToRegistryEditing()
    {
        var service = new AuthorizationService(new FakeUserContext(BuiltInRoles.Admin));

        var result = service.HasPermission(DefaultPermissions.EditWindowsRegistry);

        Assert.True(result);
    }

    [Fact]
    public void UserRole_DoesNotHaveAccessToRegistryEditing()
    {
        var service = new AuthorizationService(new FakeUserContext(BuiltInRoles.User));

        var result = service.HasPermission(DefaultPermissions.EditWindowsRegistry);

        Assert.False(result);
    }

    private sealed class FakeUserContext(params string[] roleNames) : IUserContext
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public string UserName { get; } = "test-user";

        public IReadOnlyCollection<string> RoleNames { get; } = roleNames;
    }
}
