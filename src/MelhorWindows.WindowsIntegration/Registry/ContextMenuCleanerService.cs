using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.Registry;

public sealed class ContextMenuCleanerService
{
    public sealed record ContextMenuEntry(string KeyPath, string DisplayName, string Command, string? IconPath);

    public IReadOnlyList<ContextMenuEntry> GetAll()
    {
        var entries = new List<ContextMenuEntry>();

        ReadShellEntries(entries, @"*\shell");
        ReadShellEntries(entries, @"Directory\shell");
        ReadShellEntries(entries, @"Directory\Background\shell");

        return entries;
    }

    public void Remove(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        if (parts.Length < 2) return;

        try
        {
            Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    private static void ReadShellEntries(List<ContextMenuEntry> entries, string shellPath)
    {
        using var shellKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(shellPath);
        if (shellKey is null) return;

        foreach (var subKeyName in shellKey.GetSubKeyNames())
        {
            try
            {
                using var subKey = shellKey.OpenSubKey(subKeyName);
                if (subKey is null) continue;

                var displayName = subKey.GetValue("")?.ToString() ?? subKeyName;
                var icon = subKey.GetValue("Icon")?.ToString();

                var command = "";
                using var commandKey = subKey.OpenSubKey("command");
                if (commandKey is not null)
                    command = commandKey.GetValue("")?.ToString() ?? "";

                entries.Add(new ContextMenuEntry(
                    $"{shellPath}\\{subKeyName}",
                    displayName,
                    command,
                    icon));
            }
            catch { }
        }
    }
}
