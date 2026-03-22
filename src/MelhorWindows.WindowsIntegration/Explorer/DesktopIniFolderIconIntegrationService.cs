using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MelhorWindows.Application.Abstractions;

namespace MelhorWindows.WindowsIntegration.Explorer;

public sealed class DesktopIniFolderIconIntegrationService : IFolderIconIntegrationService
{
    private const string FolderIconFilePrefix = "auralis-folder-icon-";
    private static readonly string[] LegacyFolderIconFileNames = ["melhorwindows-folder-icon.ico"];
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

        var sourceIconBytes = await File.ReadAllBytesAsync(iconFilePath, cancellationToken);
        var folderIconFileName = BuildFolderIconFileName(sourceIconBytes);
        DeletePreviousAuralisIcons(folderPath, folderIconFileName);

        var storedIconPathInFolder = Path.Combine(folderPath, folderIconFileName);
        ResetProtectedFileIfNeeded(storedIconPathInFolder);
        await File.WriteAllBytesAsync(storedIconPathInFolder, sourceIconBytes, cancellationToken);

        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");
        ResetProtectedFileIfNeeded(desktopIniPath);
        var desktopIniContent = new StringBuilder()
            .AppendLine("[.ShellClassInfo]")
            .AppendLine("ConfirmFileOp=0")
            .AppendLine($"IconResource=.\\{folderIconFileName},0")
            .ToString();

        await File.WriteAllTextAsync(desktopIniPath, desktopIniContent, Encoding.Unicode, cancellationToken);

        EnsureFolderCustomizationAttributes(folderPath);

        var iniAttributes = File.GetAttributes(desktopIniPath);
        File.SetAttributes(desktopIniPath, iniAttributes | FileAttributes.Hidden | FileAttributes.System);

        var iconAttributes = File.GetAttributes(storedIconPathInFolder);
        File.SetAttributes(storedIconPathInFolder, iconAttributes | FileAttributes.Hidden | FileAttributes.System);

        RefreshShell(folderPath, desktopIniPath, storedIconPathInFolder, Path.GetDirectoryName(folderPath));
        await RefreshExplorerIconCacheAsync(cancellationToken);
        await FinalizeShellRefreshAsync(folderPath, desktopIniPath, storedIconPathInFolder, cancellationToken);
    }

    public async Task RemoveIconAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");

        foreach (var managedIconPath in EnumerateManagedIconPaths(folderPath))
        {
            ResetProtectedFileIfNeeded(managedIconPath);
        }

        ResetProtectedFileIfNeeded(desktopIniPath);
        ClearFolderCustomizationAttributes(folderPath);

        RefreshShell(folderPath, desktopIniPath, iconPath: null, Path.GetDirectoryName(folderPath));
        await RefreshExplorerIconCacheAsync(cancellationToken);
        await FinalizeShellRefreshAsync(folderPath, desktopIniPath, iconPath: null, cancellationToken);
    }

    public async Task<bool> RepairIconReferenceAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        if (HasCanonicalManagedFolderIcon(folderPath))
        {
            return false;
        }

        var candidateIconPath = ResolveRepairSourceIconPath(folderPath);

        if (string.IsNullOrWhiteSpace(candidateIconPath) ||
            !File.Exists(candidateIconPath))
        {
            return false;
        }

        await ApplyIconAsync(folderPath, candidateIconPath, cancellationToken);
        return true;
    }

    private static string BuildFolderIconFileName(byte[] iconBytes)
    {
        var hashBytes = SHA256.HashData(iconBytes);
        var hashSuffix = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        return $"{FolderIconFilePrefix}{hashSuffix}.ico";
    }

    private static string? ResolveRepairSourceIconPath(string folderPath)
    {
        var managedLocalIconPath = Directory
            .EnumerateFiles(folderPath, $"{FolderIconFilePrefix}*.ico")
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(managedLocalIconPath))
        {
            return managedLocalIconPath;
        }

        foreach (var legacyFileName in LegacyFolderIconFileNames)
        {
            var legacyIconPath = Path.Combine(folderPath, legacyFileName);

            if (File.Exists(legacyIconPath))
            {
                return legacyIconPath;
            }
        }

        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");

        if (!File.Exists(desktopIniPath))
        {
            return null;
        }

        return ResolveDesktopIniIconPath(folderPath, desktopIniPath);
    }

    private static bool HasCanonicalManagedFolderIcon(string folderPath)
    {
        var desktopIniPath = Path.Combine(folderPath, "desktop.ini");

        if (!File.Exists(desktopIniPath))
        {
            return false;
        }

        var managedLocalIconPath = Directory
            .EnumerateFiles(folderPath, $"{FolderIconFilePrefix}*.ico")
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(managedLocalIconPath))
        {
            return false;
        }

        var iconFileName = Path.GetFileName(managedLocalIconPath);
        var expectedIconResource = $"IconResource=.\\{iconFileName},0";
        var desktopIniLines = ReadDesktopIniLines(desktopIniPath).ToArray();

        if (!desktopIniLines.Any(line => string.Equals(line.Trim(), expectedIconResource, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var folderAttributes = File.GetAttributes(folderPath);
        return folderAttributes.HasFlag(FileAttributes.ReadOnly) ||
               folderAttributes.HasFlag(FileAttributes.System);
    }

    private static string? ResolveDesktopIniIconPath(string folderPath, string desktopIniPath)
    {
        foreach (var line in ReadDesktopIniLines(desktopIniPath))
        {
            if (!TryExtractIconReferenceValue(line, "IconResource=", out var iconResourceValue) &&
                !TryExtractIconReferenceValue(line, "IconFile=", out iconResourceValue))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(iconResourceValue))
            {
                continue;
            }

            var candidatePath = iconResourceValue;

            if (!Path.IsPathRooted(candidatePath))
            {
                candidatePath = candidatePath.TrimStart('.', '\\', '/');
                candidatePath = Path.Combine(folderPath, candidatePath);
            }

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IEnumerable<string> ReadDesktopIniLines(string desktopIniPath)
    {
        try
        {
            return File.ReadAllLines(desktopIniPath, Encoding.Unicode);
        }
        catch (DecoderFallbackException)
        {
            return File.ReadAllLines(desktopIniPath);
        }
    }

    private static bool TryExtractIconReferenceValue(string line, string prefix, out string value)
    {
        value = string.Empty;

        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = line[prefix.Length..]
            .Split(',', 2)[0]
            .Trim();

        return !string.IsNullOrWhiteSpace(value);
    }

    private static void DeletePreviousAuralisIcons(string folderPath, string currentIconFileName)
    {
        foreach (var existingIconPath in EnumerateManagedIconPaths(folderPath))
        {
            if (string.Equals(Path.GetFileName(existingIconPath), currentIconFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ResetProtectedFileIfNeeded(existingIconPath);
        }
    }

    private static IEnumerable<string> EnumerateManagedIconPaths(string folderPath)
    {
        foreach (var existingIconPath in Directory.EnumerateFiles(folderPath, $"{FolderIconFilePrefix}*.ico"))
        {
            yield return existingIconPath;
        }

        foreach (var legacyFileName in LegacyFolderIconFileNames)
        {
            var legacyIconPath = Path.Combine(folderPath, legacyFileName);

            if (File.Exists(legacyIconPath))
            {
                yield return legacyIconPath;
            }
        }
    }

    private static void EnsureFolderCustomizationAttributes(string folderPath)
    {
        var folderAttributes = File.GetAttributes(folderPath);
        folderAttributes |= FileAttributes.ReadOnly;
        folderAttributes |= FileAttributes.System;
        File.SetAttributes(folderPath, folderAttributes);
    }

    private static void ClearFolderCustomizationAttributes(string folderPath)
    {
        var folderAttributes = File.GetAttributes(folderPath);
        folderAttributes &= ~FileAttributes.ReadOnly;
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

    private static void RefreshShell(
        string folderPath,
        string desktopIniPath,
        string? iconPath,
        string? parentFolderPath)
    {
        SHChangeNotify(ShcneAttributes, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateDir, ShcnfPathW, folderPath, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW, desktopIniPath, IntPtr.Zero);

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            SHChangeNotify(ShcneUpdateItem, ShcnfPathW, iconPath, IntPtr.Zero);
        }

        if (!string.IsNullOrWhiteSpace(parentFolderPath))
        {
            SHChangeNotify(ShcneUpdateDir, ShcnfPathW, parentFolderPath, IntPtr.Zero);
            SHChangeNotify(ShcneUpdateItem, ShcnfPathW, parentFolderPath, IntPtr.Zero);
        }

        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, null, IntPtr.Zero);
    }

    private static Task RefreshExplorerIconCacheAsync(CancellationToken cancellationToken)
    {
        // ie4uinit.exe -ClearIconCache foi descontinuado silenciosamente no Windows 10 1903+
        // e causa lentidão desnecessária (timeout de até 10s). O SHChangeNotify já é suficiente
        // para forçar o Explorer a reler o desktop.ini no Windows moderno.
        return Task.CompletedTask;
    }

    private static Task FinalizeShellRefreshAsync(
        string folderPath,
        string desktopIniPath,
        string? iconPath,
        CancellationToken cancellationToken)
    {
        // Second shell notification pass — no delay needed since SHChangeNotify is synchronous.
        RefreshShell(folderPath, desktopIniPath, iconPath, Path.GetDirectoryName(folderPath));
        return Task.CompletedTask;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        string? item1,
        IntPtr item2);
}
