namespace MelhorWindows.Application.Models;

public sealed record OperationResult(bool Succeeded, string Message)
{
    public static OperationResult Success(string message) => new(true, message);

    public static OperationResult Failure(string message) => new(false, message);
}

public sealed record OperationResult<T>(bool Succeeded, string Message, T? Value)
{
    public static OperationResult<T> Success(T value, string message) => new(true, message, value);

    public static OperationResult<T> Failure(string message) => new(false, message, default);
}

