using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace PulseFlow.Application;

[JsonConverter(typeof(ResultJsonConverterFactory))]
public class Result : IFailureFactory<Result>
{
    private static readonly Result Success = new(true, null);

    protected Result(bool isSuccess, Error? error)
    {
        switch (isSuccess)
        {
            case true when error != null:
                throw new InvalidOperationException("A successful result cannot contain an error.");
            case false when error == null:
                throw new InvalidOperationException("A failing result must contain an error.");
            default:
                IsSuccess = isSuccess;
                Error = error;
                break;
        }
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>The error of a failed result; <c>null</c> when successful.</summary>
    public Error? Error { get; }

    /// <summary>A successful result. Cached: no allocation.</summary>
    public static Result Ok() => Success;

    public static Result Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    /// <summary>Shortcut for <c>Fail(Error.Failure("Failure", message))</c>.</summary>
    public static Result Fail(string message) => Fail(Application.Error.Failure("Failure", message));

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    public static implicit operator Result(Error error) => Fail(error);

    static Result IFailureFactory<Result>.CreateFailure(Error error) => Fail(error);
}

[JsonConverter(typeof(ResultJsonConverterFactory))]
public class Result<T> : Result, IFailureFactory<Result<T>>
{
    private readonly T _value;

    private Result(bool isSuccess, T value, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// The value of a successful result.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is a failure; check <see cref="Result.IsSuccess"/> first
    /// or use <see cref="ValueOrDefault"/>.</exception>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException($"Cannot access the value of a failed result. Error: {Error}");

    /// <summary>
    /// The value when the result is successful; otherwise <c>default</c>. Never throws.
    /// </summary>
    public T? ValueOrDefault => IsSuccess ? _value : default;

    public static Result<T> Ok(T value) => new(true, value, null);

    public new static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(false, default!, error);
    }

    /// <summary>Shortcut for <c>Fail(Error.Failure("Failure", message))</c>.</summary>
    public new static Result<T> Fail(string message) => Fail(Application.Error.Failure("Failure", message));

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value) : onFailure(Error);
    }

    /// <summary>Transforms the value of a successful result; a failure is propagated unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsSuccess ? Result<TOut>.Ok(map(_value)) : Result<TOut>.Fail(Error);
    }

    /// <summary>Chains an operation that can itself fail; a failure is propagated unchanged.</summary>
    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return IsSuccess ? bind(_value) : Result<TOut>.Fail(Error);
    }

    /// <summary>Allows <c>return value;</c> in a handler (does not apply when <typeparamref name="T"/> is an interface).</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>Allows <c>return Error.NotFound(...);</c> in a handler.</summary>
    public static implicit operator Result<T>(Error error) => Fail(error);

    static Result<T> IFailureFactory<Result<T>>.CreateFailure(Error error) => Fail(error);
}
