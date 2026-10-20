namespace OrderService.Application.Common;

public sealed class AppResult<T>
{
    private AppResult(bool isSuccess, T? value, AppError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public AppError? Error { get; }

    public static AppResult<T> Success(T value) => new(true, value, null);

    public static AppResult<T> Failure(ErrorType type, string code, string message, string? field = null) =>
        new(false, default, new AppError(type, code, message, field));
}

