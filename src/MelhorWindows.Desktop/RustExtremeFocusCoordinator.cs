using System.Diagnostics;
using System.IO;
using System.Text;
using MelhorWindows.Infrastructure.Storage;

namespace MelhorWindows.Desktop;

internal sealed class RustExtremeFocusCoordinator(AppDataPaths appDataPaths)
{
    private static readonly HashSet<string> PreservedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auralis",
        "steam",
        "steamservice",
        "steamwebhelper",
        "gameoverlayui",
        "discord",
        "discordptb",
        "discordcanary",
        "Rust",
        "RustClient",
        "EasyAntiCheat",
        "EasyAntiCheat_EOS"
    };

    private static readonly HashSet<string> CriticalWindowsProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle",
        "System",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "winlogon",
        "services",
        "lsass",
        "svchost",
        "fontdrvhost",
        "dwm",
        "taskhostw",
        "sihost",
        "audiodg",
        "SearchIndexer",
        "WmiPrvSE",
        "spoolsv"
    };

    private const string RustSteamUri = "steam://rungameid/252490";

    public RustExtremeFocusActivationResult ActivateForRust()
    {
        appDataPaths.EnsureCreated();
        var restoreScriptPath = EnsureRestoreExplorerScript();
        var closedProcessCount = CloseNonEssentialUserProcesses();
        var explorerProcessCount = StopExplorer();
        LaunchRust();

        return new RustExtremeFocusActivationResult(
            closedProcessCount,
            explorerProcessCount,
            restoreScriptPath);
    }

    public string EnsureRestoreExplorerScript()
    {
        var scriptsDirectory = Path.Combine(appDataPaths.RootDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);

        var scriptPath = Path.Combine(scriptsDirectory, "restore-explorer.cmd");
        var scriptContent = string.Join(
            Environment.NewLine,
            [
                "@echo off",
                "start \"\" explorer.exe"
            ]);

        File.WriteAllText(scriptPath, scriptContent + Environment.NewLine, Encoding.ASCII);
        return scriptPath;
    }

    public void RestoreExplorer(string restoreScriptPath)
    {
        if (!string.IsNullOrWhiteSpace(restoreScriptPath) && File.Exists(restoreScriptPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = restoreScriptPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(restoreScriptPath) ?? Environment.CurrentDirectory
            });

            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        });
    }

    private static int CloseNonEssentialUserProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var closedCount = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcessId ||
                    process.SessionId != currentSessionId ||
                    string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                    ShouldPreserveProcess(process))
                {
                    continue;
                }

                if (!LooksSafeToTerminate(process))
                {
                    continue;
                }

                if (TryTerminateProcess(process))
                {
                    closedCount++;
                }
            }
            catch
            {
                // Ignore processes that cannot be inspected or terminated.
            }
            finally
            {
                process.Dispose();
            }
        }

        return closedCount;
    }

    private static int StopExplorer()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        var stoppedCount = 0;

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (process.SessionId != currentSessionId)
                {
                    continue;
                }

                if (TryTerminateProcess(process))
                {
                    stoppedCount++;
                }
            }
            catch
            {
                // Ignore Explorer instances we cannot close.
            }
            finally
            {
                process.Dispose();
            }
        }

        return stoppedCount;
    }

    private static void LaunchRust()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = RustSteamUri,
            UseShellExecute = true
        });
    }

    private static bool ShouldPreserveProcess(Process process)
    {
        if (PreservedProcessNames.Contains(process.ProcessName) ||
            CriticalWindowsProcessNames.Contains(process.ProcessName))
        {
            return true;
        }

        var processPath = TryGetProcessPath(process);
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return true;
        }

        if (processPath.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (processPath.Contains("\\Discord\\", StringComparison.OrdinalIgnoreCase) ||
            processPath.Contains("\\Steam\\", StringComparison.OrdinalIgnoreCase) ||
            processPath.Contains("\\Auralis\\", StringComparison.OrdinalIgnoreCase) ||
            processPath.Contains("\\Rust\\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool LooksSafeToTerminate(Process process)
    {
        return !process.HasExited &&
               process.Id > 0 &&
               !string.IsNullOrWhiteSpace(process.ProcessName);
    }

    private static bool TryTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            if (process.CloseMainWindow())
            {
                if (process.WaitForExit(900))
                {
                    return true;
                }
            }

            process.Kill(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record RustExtremeFocusActivationResult(
    int ClosedProcessCount,
    int ExplorerProcessCount,
    string RestoreScriptPath);
