using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.Registry;

public sealed class StartupManagerService
{
    public sealed record StartupEntry(string Name, string Command, string Source, bool IsEnabled);

    public IReadOnlyList<StartupEntry> GetAll()
    {
        var entries = new List<StartupEntry>();

        ReadRunKey(entries, Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", isEnabled: true);
        ReadRunKey(entries, Microsoft.Win32.Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", isEnabled: true);
        ReadRunKey(entries, Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run_Disabled", "HKCU\\Run (Disabled)", isEnabled: false);

        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(startupFolder))
        {
            foreach (var file in Directory.EnumerateFiles(startupFolder))
            {
                entries.Add(new StartupEntry(
                    Path.GetFileNameWithoutExtension(file),
                    file,
                    "Startup Folder",
                    true));
            }
        }

        return entries;
    }

    public void Disable(string name)
    {
        using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (runKey is null) return;

        var value = runKey.GetValue(name);
        if (value is null) return;

        using var disabledKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run_Disabled");
        disabledKey?.SetValue(name, value);
        runKey.DeleteValue(name, throwOnMissingValue: false);
    }

    public void Enable(string name)
    {
        using var disabledKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run_Disabled", writable: true);
        if (disabledKey is null) return;

        var value = disabledKey.GetValue(name);
        if (value is null) return;

        using var runKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        runKey?.SetValue(name, value);
        disabledKey.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void ReadRunKey(List<StartupEntry> entries, RegistryKey root, string path, string source, bool isEnabled)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;

        foreach (var name in key.GetValueNames())
        {
            var command = key.GetValue(name)?.ToString() ?? "";
            entries.Add(new StartupEntry(name, command, source, isEnabled));
        }
    }
}
