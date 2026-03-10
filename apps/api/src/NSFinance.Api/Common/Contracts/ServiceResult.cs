namespace NSFinance.Api.Common.Contracts;

public sealed record ServiceError(string Message, string Code, int StatusCode);

public class ServiceResult
{
    public ServiceError? Error { get; init; }
    public bool Succeeded => Error is null;

    public static ServiceResult Ok() => new();

    public static ServiceResult Fail(string message, string code, int statusCode) =>
        new() { Error = new ServiceError(message, code, statusCode) };
}

public class ServiceResult<T>
{
    public T? Value { get; init; }
    public ServiceError? Error { get; init; }
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Ok(T value) => new() { Value = value };

    public static ServiceResult<T> Fail(string message, string code, int statusCode) =>
        new() { Error = new ServiceError(message, code, statusCode) };
}
