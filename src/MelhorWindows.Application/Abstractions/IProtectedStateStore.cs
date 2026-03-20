namespace MelhorWindows.Application.Abstractions;

public interface IProtectedStateStore
{
    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

