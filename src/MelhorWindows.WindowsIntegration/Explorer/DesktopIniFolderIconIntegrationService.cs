using System.Runtime.InteropServices;
using System.Text;
using MelhorWindows.Application.Abstractions;
using System.Diagnostics;

namespace MelhorWindows.WindowsIntegration.Explorer;

public sealed class DesktopIniFolderIconIntegrationService : IFolderIconIntegrationService
{
    private const string FolderIconFileName = "melhorwindows-folder-icon.ico";
    private const uint ShcneAttributes = 0x00000800;
    private const uint ShcneUpdateItem = 0x00002000;
    private const uint ShcneUpdateDir = 0x00001000;
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfPathW = 0x0005;
    private const uint ShcnfIdList = 0x0000;

    public async Task ApplyIconAsync(
        string folderPath,
        string iconFilePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        if (!File.Exists(iconFilePath))
        {
            throw new FileNotFoundException("Icon file not found.", iconFilePath);
        }

        var storedIconPathInFolder = Path.Combine(folderPath, FolderIconFileName);
        ResetProtectedFileIfNeeded(storedIconPathInFolder);
        await CopyIconIntoFolderAsync(iconFilePath, storedIconPathInFolder, cancellationToken);

        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");
        ResetProtectedFileIfNeeded(desktopIniPath);
        var desktopIniContent = new StringBuilder()
            .AppendLine("[.ShellClassInfo]")
            .AppendLine("ConfirmFileOp=0")
            .AppendLine($"IconResource={FolderIconFileName},0")
            .AppendLine($"IconFile={FolderIconFileName}")
            .AppendLine("IconIndex=0")
            .ToString();

        await File.WriteAllTextAsync(desktopIniPath, desktopIniContent, Encoding.Unicode, cancellationToken);

        EnsureFolderCustomizationAttributes(folderPath);

        var iniAttributes = File.GetAttributes(desktopIniPath);
        File.SetAttributes(desktopIniPath, iniAttributes | FileAttributes.Hidden | FileAttributes.System);

        var iconAttributes = File.GetAttributes(storedIconPathInFolder);
        File.SetAttributes(storedIconPathInFolder, iconAttributes | FileAttributes.Hidden | FileAttributes.System);

        RefreshShell(folderPath, desktopIniPath, storedIconPathInFolder, Path.GetDirectoryName(folderPath));
        await RefreshExplorerIconCacheAsync(cancellationToken);
    }

    private static void EnsureFolderCustomizationAttributes(string folderPath)
    {
        var folderAttributes = File.GetAttributes(folderPath);
        folderAttributes |= FileAttributes.ReadOnly;
        folderAttributes &= ~FileAttributes.System;
        File.SetAttributes(folderPath, folderAttributes);
    }

    private static void ResetProtectedFileIfNeeded(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        File.SetAttributes(filePath, FileAttributes.Normal);
        File.Delete(filePath);
    }

    private static async Task CopyIconIntoFolderAsync(
        string sourceIconPath,
        string targetIconPath,
        CancellationToken cancellationToken)
    {
        await using var sourceStream = File.Open(sourceIconPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var targetStream = File.Create(targetIconPath);
        await sourceStream.CopyToAsync(targetStream, cancellationToken);
        await targetStream.FlushAsync(cancellationToken);
    }

    private static void RefreshShell(
        string folderPath,
        string desktopIniPath,
        string iconPath,
        string? parentFolderPath)
    {
        SHChangeNotify(ShcneAttributes, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateDir, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW, desktopIniPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW, iconPath, IntPtr.Zero);

        if (!string.IsNullOrWhiteSpace(parentFolderPath))
        {
            SHChangeNotify(ShcneUpdateDir, ShcnfPathW, parentFolderPath, IntPtr.Zero);
            SHChangeNotify(ShcneUpdateItem, ShcnfPathW, parentFolderPath, IntPtr.Zero);
        }

        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, null, IntPtr.Zero);
    }

    private static async Task RefreshExplorerIconCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var refreshToolPath = Path.Combine(Environment.SystemDirectory, "ie4uinit.exe");

            if (!File.Exists(refreshToolPath))
            {
                return;
            }

            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = refreshToolPath,
                    Arguments = "-ClearIconCache",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

            if (process is null)
            {
                return;
            }

            // Evita travar a thread de UI enquanto o Explorer atualiza o cache.
            // Usamos timeout via CancellationToken porque nem todos os alvos expõem o overload com TimeSpan.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // timeout best-effort
            }
        }
        catch
        {
            // Shell refresh is best-effort only.
        }

        try
        {
            var refreshToolPath = Path.Combine(Environment.SystemDirectory, "ie4uinit.exe");

            if (!File.Exists(refreshToolPath))
            {
                return;
            }

            using var process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = refreshToolPath,
                    Arguments = "-show",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

            if (process is null)
            {
                return;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // timeout best-effort
            }
        }
        catch
        {
            // Shell refresh is best-effort only.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        string? item1,
        IntPtr item2);
}
