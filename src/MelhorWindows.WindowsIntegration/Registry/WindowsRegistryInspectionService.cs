using MelhorWindows.Application.Abstractions;
using Microsoft.Win32;

namespace MelhorWindows.WindowsIntegration.Registry;

public sealed class WindowsRegistryInspectionService : IRegistryInspectionService
{
    public Task<object?> GetValueAsync(
        RegistryHive hive,
        string keyPath,
        string valueName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        using var targetKey = baseKey.OpenSubKey(keyPath, writable: false);

        return Task.FromResult(targetKey?.GetValue(valueName));
    }
}
