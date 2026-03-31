using MelhorWindows.Application.Abstractions;
using MelhorWindows.Domain.Authorization;
using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.Registry;

public sealed class FolderMonitorRegistrationService
{
    private const string RegPathFolder = @"Directory\shell\FolderMonitor";
    private const string RegPathBackground = @"Directory\Background\shell\FolderMonitor";

    private readonly IAuthorizationService _authorizationService;

    public FolderMonitorRegistrationService(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    public bool IsInstalled()
    {
        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(RegPathFolder);
        return key is not null;
    }

    public void Install(string workerExePath)
    {
        _authorizationService.EnsurePermission(DefaultPermissions.RegisterExplorerIntegration);

        if (!global::System.IO.File.Exists(workerExePath))
            throw new global::System.IO.FileNotFoundException("Worker executable not found.", workerExePath);

        InstallKey(RegPathFolder, workerExePath, "%1");
        InstallKey(RegPathBackground, workerExePath, "%V");
    }

    public void Uninstall()
    {
        _authorizationService.EnsurePermission(DefaultPermissions.RegisterExplorerIntegration);
        try { Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(RegPathFolder, throwOnMissingSubKey: false); } catch { }
        try { Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(RegPathBackground, throwOnMissingSubKey: false); } catch { }
    }

    private static void InstallKey(string regPath, string exePath, string argVariable)
    {
        using var key = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey(regPath);
        if (key is null) return;

        key.SetValue("", "Monitorar essa pasta");
        key.SetValue("Icon", exePath);

        using var commandKey = key.CreateSubKey("command");
        commandKey?.SetValue("", $"\"{exePath}\" \"{argVariable}\"");
    }
}
