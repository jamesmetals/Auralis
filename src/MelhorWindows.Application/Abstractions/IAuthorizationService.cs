namespace MelhorWindows.Application.Abstractions;

public interface IAuthorizationService
{
    bool HasPermission(string permission);

    void EnsurePermission(string permission);
}

