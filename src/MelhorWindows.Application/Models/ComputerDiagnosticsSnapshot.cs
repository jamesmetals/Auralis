using System.Collections.Generic;

namespace MelhorWindows.Application.Models;

public sealed record ProcessResourceUsageItem(
    string ProcessName,
    double MemoryUsedGb);

public sealed record ComputerDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    string CpuLabel,
    int LogicalCoreCount,
    string WindowsVersion,
    double CpuUsagePercent,
    double MemoryLoadPercent,
    double MemoryUsedGb,
    double MemoryAvailableGb,
    double MemoryTotalGb,
    string GpuLabel,
    IReadOnlyList<ProcessResourceUsageItem> TopMemoryProcesses);

