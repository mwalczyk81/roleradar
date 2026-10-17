using System.Diagnostics.CodeAnalysis;

namespace RoleRadar.Application.Abstractions;

public enum ErrorCode
{
    ValidationError,
    NotFound,
    StaleVersionToken,
    SectionNotFound,
    AmbiguousHeading,
    ImportConflict,
    InvalidUtf8,
    DatastoreOffline
}

public readonly record struct ResultError(
    ErrorCode Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public readonly record struct Result<T>
{
    public bool IsSuccess { get; }

    public T? Value { get; }

    public ResultError? Error { get; }

    private Result(bool isSuccess, T? value, ResultError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Contract specifies Result<T>.Success / Result<T>.Failure static factory methods.")]
    public static Result<T> Success(T value) => new(true, value, null);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Contract specifies Result<T>.Success / Result<T>.Failure static factory methods.")]
    public static Result<T> Failure(ResultError error) => new(false, default, error);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Contract specifies Result<T>.Success / Result<T>.Failure static factory methods.")]
    public static Result<T> Failure(ErrorCode code, string message, IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(false, default, new ResultError(code, message, metadata));
}
