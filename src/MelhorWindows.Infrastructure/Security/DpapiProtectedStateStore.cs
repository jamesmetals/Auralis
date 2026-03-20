using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Infrastructure.Storage;

namespace MelhorWindows.Infrastructure.Security;

public sealed class DpapiProtectedStateStore(AppDataPaths appDataPaths) : IProtectedStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        appDataPaths.EnsureCreated();

        var targetPath = BuildPath(key);
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        var clearBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(clearBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

        await File.WriteAllBytesAsync(targetPath, encryptedBytes, cancellationToken);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var targetPath = BuildPath(key);

        if (!File.Exists(targetPath))
        {
            return default;
        }

        var encryptedBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
        var clearBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(clearBytes);

        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var targetPath = BuildPath(key);

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        return Task.CompletedTask;
    }

    private string BuildPath(string key)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(key.Select(character => invalidChars.Contains(character) ? '-' : character).ToArray());

        return Path.Combine(appDataPaths.SecureStateDirectory, $"{fileName}.bin");
    }
}
