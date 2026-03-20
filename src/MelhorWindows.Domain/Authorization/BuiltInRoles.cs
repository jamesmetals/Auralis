namespace MelhorWindows.Domain.Authorization;

public static class BuiltInRoles
{
    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string User = "user";

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> PermissionMatrix { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Admin] = new HashSet<string>(DefaultPermissions.All, StringComparer.OrdinalIgnoreCase),
            [Operator] = new HashSet<string>(
                [
                    DefaultPermissions.ApplyFolderIcons,
                    DefaultPermissions.ViewOwnIconHistory,
                    DefaultPermissions.SyncOwnIconHistory,
                    DefaultPermissions.RegisterExplorerIntegration
                ],
                StringComparer.OrdinalIgnoreCase),
            [User] = new HashSet<string>(
                [
                    DefaultPermissions.ApplyFolderIcons,
                    DefaultPermissions.ViewOwnIconHistory,
                    DefaultPermissions.SyncOwnIconHistory
                ],
                StringComparer.OrdinalIgnoreCase)
        };
}

