using Microsoft.Win32;

namespace MelhorWindows.Application.Abstractions;

public interface IRegistryInspectionService
{
    Task<object?> GetValueAsync(
        RegistryHive hive,
        string keyPath,
        string valueName,
        CancellationToken cancellationToken = default);
}
