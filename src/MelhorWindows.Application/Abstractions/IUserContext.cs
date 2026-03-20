namespace MelhorWindows.Application.Abstractions;

public interface IUserContext
{
    Guid UserId { get; }

    string UserName { get; }

    IReadOnlyCollection<string> RoleNames { get; }
}

