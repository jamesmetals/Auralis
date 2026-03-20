using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Authorization;
using Microsoft.Win32;
using Win32Registry = Microsoft.Win32.Registry;

namespace MelhorWindows.WindowsIntegration.Explorer;

public sealed class ExplorerVerbRegistrationService(IAuthorizationService authorizationService) : IExplorerVerbRegistrationService
{
    private const string VerbName = "Auralis";
    private const string LegacyVerbName = "Auralis.ChangeFolderIcon";
    private const string VerbLabel = "Auralis";

    public Task RegisterFolderVerbAsync(string appExecutablePath, CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.RegisterExplorerIntegration);

        RegisterVerb(@"Software\Classes\Directory\shell", appExecutablePath);
        DeleteVerb(@"Software\Classes\Folder\shell");

        return Task.CompletedTask;
    }

    public Task UnregisterFolderVerbAsync(CancellationToken cancellationToken = default)
    {
        authorizationService.EnsurePermission(DefaultPermissions.RegisterExplorerIntegration);

        DeleteVerb(@"Software\Classes\Directory\shell");
        DeleteVerb(@"Software\Classes\Folder\shell");

        return Task.CompletedTask;
    }

    private static void RegisterVerb(string parentPath, string appExecutablePath)
    {
        using var shellRoot = Win32Registry.CurrentUser.CreateSubKey(parentPath, true);
        using var verbKey = shellRoot?.CreateSubKey(VerbName, true);

        verbKey?.SetValue("MUIVerb", VerbLabel);
        verbKey?.SetValue("Icon", appExecutablePath);
        verbKey?.SetValue("Position", "Top");
        verbKey?.SetValue("MultiSelectModel", "Single");
        verbKey?.SetValue("NeverDefault", string.Empty);
        verbKey?.SetValue("SeparatorBefore", string.Empty);
        verbKey?.SetValue("SeparatorAfter", string.Empty);
        verbKey?.DeleteValue("SubCommands", throwOnMissingValue: false);
        verbKey?.DeleteSubKeyTree("shell", throwOnMissingSubKey: false);

        using var commandKey = verbKey?.CreateSubKey("command", true);
        commandKey?.SetValue(string.Empty, $"\"{appExecutablePath}\" \"%1\"");
    }

    private static void DeleteVerb(string parentPath)
    {
        using var shellRoot = Win32Registry.CurrentUser.OpenSubKey(parentPath, writable: true);
        shellRoot?.DeleteSubKeyTree(VerbName, throwOnMissingSubKey: false);
        shellRoot?.DeleteSubKeyTree(LegacyVerbName, throwOnMissingSubKey: false);
    }
}
