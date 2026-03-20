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
    private const string ChangeIconCommandName = "change-icon";
    private const string OpenDashboardCommandName = "open-dashboard";
    private const string CheckUpdatesCommandName = "check-updates";

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
        verbKey?.SetValue("SubCommands", string.Empty);

        using var submenuRoot = verbKey?.CreateSubKey("shell", true);
        RegisterSubCommand(
            submenuRoot,
            ChangeIconCommandName,
            "Trocar icone da pasta",
            appExecutablePath,
            $"\"{appExecutablePath}\" \"%1\"");
        RegisterSubCommand(
            submenuRoot,
            OpenDashboardCommandName,
            "Abrir painel do Auralis",
            appExecutablePath,
            $"\"{appExecutablePath}\"");
        RegisterSubCommand(
            submenuRoot,
            CheckUpdatesCommandName,
            "Verificar atualizacoes",
            appExecutablePath,
            $"\"{appExecutablePath}\" --check-updates");
    }

    private static void RegisterSubCommand(
        RegistryKey? submenuRoot,
        string commandKeyName,
        string label,
        string appExecutablePath,
        string commandValue)
    {
        using var itemKey = submenuRoot?.CreateSubKey(commandKeyName, true);
        itemKey?.SetValue("MUIVerb", label);
        itemKey?.SetValue("Icon", appExecutablePath);

        using var commandKey = itemKey?.CreateSubKey("command", true);
        commandKey?.SetValue(string.Empty, commandValue);
    }

    private static void DeleteVerb(string parentPath)
    {
        using var shellRoot = Win32Registry.CurrentUser.OpenSubKey(parentPath, writable: true);
        shellRoot?.DeleteSubKeyTree(VerbName, throwOnMissingSubKey: false);
        shellRoot?.DeleteSubKeyTree(LegacyVerbName, throwOnMissingSubKey: false);
    }
}
