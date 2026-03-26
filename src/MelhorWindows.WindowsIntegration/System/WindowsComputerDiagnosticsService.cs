using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Diagnostics;
using System.Linq;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;
using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.System;

[SupportedOSPlatform("windows")]
public sealed class WindowsComputerDiagnosticsService : IComputerDiagnosticsService
{
    public async Task<ComputerDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpuLabel = ReadCpuLabel();
        var logicalCoreCount = Environment.ProcessorCount;
        var windowsVersion = Environment.OSVersion.VersionString;
        var memoryStatus = ReadMemoryStatus();
        var cpuUsage = await SampleCpuUsageAsync(cancellationToken);
        var totalMemoryGb = BytesToGigabytes(memoryStatus.ullTotalPhys);
        var availableMemoryGb = BytesToGigabytes(memoryStatus.ullAvailPhys);
        var usedMemoryGb = Math.Max(0, totalMemoryGb - availableMemoryGb);
        var gpuLabel = ReadGpuLabel();
        var topProcesses = GetTopMemoryProcesses();

        return new ComputerDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            cpuLabel,
            logicalCoreCount,
            windowsVersion,
            cpuUsage,
            memoryStatus.dwMemoryLoad,
            usedMemoryGb,
            availableMemoryGb,
            totalMemoryGb,
            gpuLabel,
            topProcesses);
    }

    private static string ReadGpuLabel()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var gpuKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinSAT", writable: false);
            return gpuKey?.GetValue("PrimaryAdapterString") as string ?? "GPU nao identificada";
        }
        catch
        {
            return "GPU nao acessivel";
        }
    }

    private static IReadOnlyList<ProcessResourceUsageItem> GetTopMemoryProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p => p.WorkingSet64 > 0)
                .OrderByDescending(p => p.WorkingSet64)
                .Take(10)
                .Select(p => new ProcessResourceUsageItem(p.ProcessName, BytesToGigabytes((ulong)p.WorkingSet64)))
                .ToList();
        }
        catch
        {
            return Array.Empty<ProcessResourceUsageItem>();
        }
    }

    private static string ReadCpuLabel()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var cpuKey = baseKey.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);

        return cpuKey?.GetValue("ProcessorNameString") as string ??
               Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ??
               "CPU nao identificada";
    }

    private static MemoryStatusEx ReadMemoryStatus()
    {
        var memoryStatus = new MemoryStatusEx();

        if (!GlobalMemoryStatusEx(memoryStatus))
        {
            throw new InvalidOperationException("Nao foi possivel ler o estado atual da memoria do Windows.");
        }

        return memoryStatus;
    }

    private static double BytesToGigabytes(ulong bytes)
    {
        return Math.Round(bytes / 1024d / 1024d / 1024d, 1, MidpointRounding.AwayFromZero);
    }

    private static async Task<double> SampleCpuUsageAsync(CancellationToken cancellationToken)
    {
        var firstSample = ReadCpuTimes();
        await Task.Delay(220, cancellationToken);
        var secondSample = ReadCpuTimes();

        var idleDelta = secondSample.IdleTime - firstSample.IdleTime;
        var kernelDelta = secondSample.KernelTime - firstSample.KernelTime;
        var userDelta = secondSample.UserTime - firstSample.UserTime;
        var totalDelta = kernelDelta + userDelta;

        if (totalDelta <= 0)
        {
            return 0;
        }

        var usage = (1d - idleDelta / (double)totalDelta) * 100d;
        return Math.Round(Math.Clamp(usage, 0d, 100d), 1, MidpointRounding.AwayFromZero);
    }

    private static CpuTimesSnapshot ReadCpuTimes()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            throw new InvalidOperationException("Nao foi possivel ler os tempos globais da CPU.");
        }

        return new CpuTimesSnapshot(
            ToUInt64(idleTime),
            ToUInt64(kernelTime),
            ToUInt64(userTime));
    }

    private static ulong ToUInt64(FileTime fileTime)
    {
        return ((ulong)(uint)fileTime.dwHighDateTime << 32) | (uint)fileTime.dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

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

    private sealed record CpuTimesSnapshot(
        ulong IdleTime,
        ulong KernelTime,
        ulong UserTime);
}
