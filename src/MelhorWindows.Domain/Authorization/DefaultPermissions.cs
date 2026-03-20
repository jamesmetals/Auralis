namespace MelhorWindows.Domain.Authorization;

public static class DefaultPermissions
{
    public const string ManageUsers = "users.manage";
    public const string ManageRoles = "roles.manage";
    public const string ManageLicenses = "licenses.manage";
    public const string ManageDevices = "devices.manage";
    public const string ManageGlobalSettings = "settings.global.manage";
    public const string ApplyFolderIcons = "folder-icons.apply";
    public const string ViewOwnIconHistory = "folder-icons.history.view.own";
    public const string SyncOwnIconHistory = "folder-icons.history.sync.own";
    public const string EditWindowsRegistry = "windows.registry.edit";
    public const string RegisterExplorerIntegration = "windows.explorer.integration.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        ManageUsers,
        ManageRoles,
        ManageLicenses,
        ManageDevices,
        ManageGlobalSettings,
        ApplyFolderIcons,
        ViewOwnIconHistory,
        SyncOwnIconHistory,
        EditWindowsRegistry,
        RegisterExplorerIntegration
    ];
}

