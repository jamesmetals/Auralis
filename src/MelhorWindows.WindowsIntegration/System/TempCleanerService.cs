namespace MelhorWindows.WindowsIntegration.System;

public sealed class TempCleanerService
{
    public sealed record CleanResult(int FilesDeleted, long BytesFreed, int FilesSkipped);

    public CleanResult Scan()
    {
        var paths = GetTempPaths();
        long totalBytes = 0;
        int totalFiles = 0;

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in EnumerateFilesSafe(dir))
            {
                try
                {
                    totalBytes += new FileInfo(file).Length;
                    totalFiles++;
                }
                catch { }
            }
        }

        return new CleanResult(totalFiles, totalBytes, 0);
    }

    public CleanResult Clean()
    {
        var paths = GetTempPaths();
        int deleted = 0;
        int skipped = 0;
        long freed = 0;

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in EnumerateFilesSafe(dir))
            {
                try
                {
                    var info = new FileInfo(file);
                    var size = info.Length;
                    info.Delete();
                    freed += size;
                    deleted++;
                }
                catch
                {
                    skipped++;
                }
            }

            foreach (var subDir in EnumerateDirectoriesSafe(dir))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                        Directory.Delete(subDir, recursive: false);
                }
                catch { }
            }
        }

        return new CleanResult(deleted, freed, skipped);
    }

    private static string[] GetTempPaths()
    {
        return new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
            @"C:\Windows\Temp"
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories); }
        catch { yield break; }
        foreach (var f in files)
        {
            yield return f;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string directory)
    {
        try { return Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Reverse(); }
        catch { return Enumerable.Empty<string>(); }
    }
}
