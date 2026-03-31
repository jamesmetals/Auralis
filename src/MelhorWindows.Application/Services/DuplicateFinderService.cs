using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;

namespace MelhorWindows.Application.Services;

public sealed class DuplicateFinderService
{
    public sealed record DuplicateGroup(string Hash, long FileSize, IReadOnlyList<string> Paths);
    public sealed record DuplicateCleanupResult(int FilesMoved, long BytesRecovered, int FilesSkipped);

    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(string folderPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(folderPath))
            return Array.Empty<DuplicateGroup>();

        progress?.Report("Listando arquivos...");

        var files = Directory.EnumerateFiles(folderPath, "*", System.IO.SearchOption.AllDirectories)
            .Select(f =>
            {
                try { return new FileInfo(f); }
                catch { return null; }
            })
            .Where(f => f is not null && f.Length > 0)
            .ToList();

        progress?.Report($"Agrupando {files.Count} arquivos por tamanho...");

        var sizeGroups = files
            .GroupBy(f => f!.Length)
            .Where(g => g.Count() > 1)
            .ToList();

        var duplicates = new List<DuplicateGroup>();
        int processed = 0;

        foreach (var group in sizeGroups)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            progress?.Report($"Verificando grupo {processed}/{sizeGroups.Count} ({group.Key} bytes)...");

            var hashGroups = new Dictionary<string, List<string>>();

            foreach (var file in group)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var hash = await ComputeHashAsync(file!.FullName, ct);
                    if (!hashGroups.ContainsKey(hash))
                        hashGroups[hash] = new List<string>();
                    hashGroups[hash].Add(file.FullName);
                }
                catch { }
            }

            foreach (var (hash, paths) in hashGroups)
            {
                if (paths.Count > 1)
                    duplicates.Add(new DuplicateGroup(hash, group.Key, paths));
            }
        }

        return duplicates;
    }

    public DuplicateCleanupResult RemoveDuplicates(
        IReadOnlyList<DuplicateGroup> groups,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var filesMoved = 0;
        var filesSkipped = 0;
        long bytesRecovered = 0;

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var group = groups[groupIndex];
            var orderedPaths = group.Paths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path.Length)
                .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (orderedPaths.Length < 2)
            {
                continue;
            }

            progress?.Report($"Corrigindo grupo {groupIndex + 1}/{groups.Count} de duplicatas...");

            foreach (var duplicatePath in orderedPaths.Skip(1))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    FileSystem.DeleteFile(
                        duplicatePath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);

                    filesMoved++;
                    bytesRecovered += group.FileSize;
                }
                catch
                {
                    filesSkipped++;
                }
            }
        }

        return new DuplicateCleanupResult(filesMoved, bytesRecovered, filesSkipped);
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }
}
