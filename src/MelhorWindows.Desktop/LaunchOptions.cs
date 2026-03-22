using System.IO;

namespace MelhorWindows.Desktop;

internal sealed record LaunchOptions(
    string? FolderPath,
    bool RegisterFolderVerb,
    bool UnregisterFolderVerb,
    bool CheckForUpdates,
    bool OpenDashboard)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? folderPath = null;
        var registerFolderVerb = false;
        var unregisterFolderVerb = false;
        var checkForUpdates = false;
        var openDashboard = false;

        foreach (var rawArgument in args.Skip(1))
        {
            if (Directory.Exists(rawArgument))
            {
                folderPath ??= rawArgument;
                continue;
            }

            if (string.Equals(rawArgument, "--register-folder-verb", StringComparison.OrdinalIgnoreCase))
            {
                registerFolderVerb = true;
                continue;
            }

            if (string.Equals(rawArgument, "--unregister-folder-verb", StringComparison.OrdinalIgnoreCase))
            {
                unregisterFolderVerb = true;
                continue;
            }

            if (string.Equals(rawArgument, "--check-updates", StringComparison.OrdinalIgnoreCase))
            {
                checkForUpdates = true;
                continue;
            }

            if (string.Equals(rawArgument, "--open-dashboard", StringComparison.OrdinalIgnoreCase))
            {
                openDashboard = true;
            }
        }

        return new LaunchOptions(folderPath, registerFolderVerb, unregisterFolderVerb, checkForUpdates, openDashboard);
    }
}
