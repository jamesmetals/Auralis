using System.IO;

namespace MelhorWindows.Desktop;

internal static class ShortcutMigration
{
    private const string DashboardArgument = "--open-dashboard";

    private static readonly Environment.SpecialFolder[] ShortcutRoots =
    [
        Environment.SpecialFolder.Desktop,
        Environment.SpecialFolder.CommonDesktopDirectory,
        Environment.SpecialFolder.Programs,
        Environment.SpecialFolder.CommonPrograms
    ];

    private static readonly string[] SupportedExecutableNames =
    [
        "Auralis.exe",
        "MelhorWindows.Desktop.exe"
    ];

    public static void EnsureDashboardShortcuts()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");

            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;

            foreach (var shortcutPath in EnumerateShortcutPaths())
            {
                TryUpdateShortcut(shell, shortcutPath);
            }
        }
        catch
        {
            // A falha na migracao do atalho nao deve impedir a abertura do app.
        }
    }

    private static IEnumerable<string> EnumerateShortcutPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var specialFolder in ShortcutRoots)
        {
            var rootPath = Environment.GetFolderPath(specialFolder);

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var shortcutPath in Directory.EnumerateFiles(rootPath, "*.lnk", SearchOption.AllDirectories))
            {
                if (seen.Add(shortcutPath))
                {
                    yield return shortcutPath;
                }
            }
        }
    }

    private static void TryUpdateShortcut(dynamic shell, string shortcutPath)
    {
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            var targetPath = Convert.ToString(shortcut.TargetPath) ?? string.Empty;

            if (!IsSupportedTarget(targetPath))
            {
                return;
            }

            var arguments = Convert.ToString(shortcut.Arguments) ?? string.Empty;

            if (arguments.Contains(DashboardArgument, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            shortcut.Arguments = string.IsNullOrWhiteSpace(arguments)
                ? DashboardArgument
                : $"{arguments} {DashboardArgument}";
            shortcut.Save();
        }
        catch
        {
            // Ignora atalhos inacessiveis ou em formatos inesperados.
        }
    }

    private static bool IsSupportedTarget(string targetPath)
    {
        var executableName = Path.GetFileName(targetPath);

        return SupportedExecutableNames.Contains(executableName, StringComparer.OrdinalIgnoreCase);
    }
}
