using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Authorization;

namespace MelhorWindows.Application.Services;

public sealed class AuthorizationService(IUserContext userContext) : IAuthorizationService
{
    public bool HasPermission(string permission)
    {
        foreach (var roleName in userContext.RoleNames)
        {
            if (BuiltInRoles.PermissionMatrix.TryGetValue(roleName, out var permissions) &&
                permissions.Contains(permission))
            {
                return true;
            }
        }

        return false;
    }

    public void EnsurePermission(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new UnauthorizedAccessException(
                $"The current user does not have the required permission: {permission}");
        }
    }
}

