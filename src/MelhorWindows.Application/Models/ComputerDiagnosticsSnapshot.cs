namespace MelhorWindows.Application.Models;

public sealed record ComputerDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    string CpuLabel,
    int LogicalCoreCount,
    string WindowsVersion,
    double CpuUsagePercent,
    double MemoryLoadPercent,
    double MemoryUsedGb,
    double MemoryAvailableGb,
    double MemoryTotalGb);
