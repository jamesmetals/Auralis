using System.Runtime.Versioning;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MelhorWindows.Application.Services;

[SupportedOSPlatform("windows")]
public sealed class RustGameProfileService : IRustGameProfileService
{
    public Task<RustGameProfileSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpuLabel = ReadCpuLabel();
        var totalRamGb = DetectTotalRamGb();
        var avoidHighPriority = cpuLabel.Contains("X3D", StringComparison.OrdinalIgnoreCase);
        var launchOptions = BuildLaunchOptions(totalRamGb, avoidHighPriority);
        var gcBufferCommand = totalRamGb >= 32 ? "gc.buffer 4096" : totalRamGb >= 16 ? "gc.buffer 2048" : "gc.buffer 1024";
        var clientConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Facepunch",
            "Rust",
            "cfg",
            "client.cfg");
        var clientConfigDetected = File.Exists(clientConfigPath);
        var steamLocalConfigPath = FindSteamLocalConfigPath();
        var steamConfigDetected = !string.IsNullOrWhiteSpace(steamLocalConfigPath);
        var recommendedCommands = BuildRecommendedCommands(gcBufferCommand, totalRamGb);
        var optionalCommands = BuildOptionalCommands();

        var summary = BuildSummary(totalRamGb, avoidHighPriority, clientConfigDetected, steamConfigDetected);

        var snapshot = new RustGameProfileSnapshot(
            cpuLabel,
            totalRamGb,
            totalRamGb >= 32 ? "32 GB ou mais" : totalRamGb >= 16 ? "16 GB" : "8 GB ou menos",
            avoidHighPriority,
            launchOptions,
            gcBufferCommand,
            clientConfigPath,
            clientConfigDetected,
            steamLocalConfigPath,
            steamConfigDetected,
            summary,
            recommendedCommands,
            optionalCommands);

        return Task.FromResult(snapshot);
    }

    private static string ReadCpuLabel()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var cpuKey = baseKey.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);

        return cpuKey?.GetValue("ProcessorNameString") as string ??
               Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ??
               "CPU nao identificada";
    }

    private static int DetectTotalRamGb()
    {
        var memoryStatus = new MemoryStatusEx();

        if (!GlobalMemoryStatusEx(memoryStatus))
        {
            return 16;
        }

        var totalPhysicalMemoryBytes = memoryStatus.ullTotalPhys;
        var gigabytes = (int)Math.Max(4, Math.Floor(totalPhysicalMemoryBytes / 1024d / 1024d / 1024d));
        return gigabytes;
    }

    private static string BuildLaunchOptions(int totalRamGb, bool avoidHighPriority)
    {
        var maxMem = totalRamGb switch
        {
            >= 32 => 24576,
            >= 16 => 12288,
            _ => 6144
        };

        var options = new List<string>
        {
            $"-maxMem={maxMem}",
            "-malloc=system",
            "-nolog",
            "-no-browser"
        };

        if (!avoidHighPriority)
        {
            options.Insert(0, "-high");
        }

        return string.Join(" ", options);
    }

    private static string? FindSteamLocalConfigPath()
    {
        var candidateRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "userdata"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "userdata"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "userdata")
        };

        foreach (var root in candidateRoots.Where(Directory.Exists))
        {
            var configPath = Directory
                .EnumerateFiles(root, "localconfig.vdf", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}config{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                return configPath;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildRecommendedCommands(string gcBufferCommand, int totalRamGb)
    {
        var commands = new List<string>
        {
            gcBufferCommand,
            "occlusion true",
            "graphics.damage false",
            "graphics.branding false"
        };

        if (totalRamGb <= 8)
        {
            commands.Add("grass.on false");
        }

        return commands;
    }

    private static IReadOnlyList<string> BuildOptionalCommands()
    {
        return
        [
            "fps.limit <Hz do monitor + 10>",
            "shadowquality 0",
            "shadow.distance 0",
            "graphics.drawdistance <ajustar pela GPU>"
        ];
    }

    private static string BuildSummary(
        int totalRamGb,
        bool avoidHighPriority,
        bool clientConfigDetected,
        bool steamConfigDetected)
    {
        var memoryNote = totalRamGb switch
        {
            >= 32 => "Memoria suficiente para preset de Rust mais folgado.",
            >= 16 => "Memoria adequada para Rust com margem razoavel para o sistema.",
            _ => "Memoria no limite para Rust moderno; convem reduzir agressivamente o consumo extra."
        };

        var priorityNote = avoidHighPriority
            ? "CPU com perfil X3D detectado; evitar `-high` por padrao."
            : "Pode usar `-high` no launch options se o restante do sistema estiver estavel.";

        var fileNote = clientConfigDetected || steamConfigDetected
            ? "Arquivos locais do Rust/Steam foram encontrados para a proxima fase de automacao."
            : "Ainda nao localizei arquivos do Rust ou do Steam neste perfil.";

        return $"{memoryNote} {priorityNote} {fileNote}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
