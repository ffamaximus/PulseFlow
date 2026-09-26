using Microsoft.AspNetCore.Http;
using PulseFlow.Application;

namespace PulseFlow.AspNetCore;

/// <summary>Turns <see cref="Result"/>, <see cref="Result{T}"/> and <see cref="Error"/> into minimal API results.</summary>
public static class ResultHttpExtensions
{
    /// <summary>Success: <c>204 No Content</c>. Failure: ProblemDetails with the status mapped from <see cref="Error.Type"/>.</summary>
    public static IResult ToHttpResult(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.NoContent() : new ErrorHttpResult(result.Error);
    }

    /// <summary>Success: <c>200 OK</c> with the value. Failure: ProblemDetails with the status mapped from <see cref="Error.Type"/>.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : new ErrorHttpResult(result.Error);
    }

    /// <summary>Success: the result of <paramref name="onSuccess"/>. Failure: ProblemDetails.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);
        return result.IsSuccess ? onSuccess(result.Value) : new ErrorHttpResult(result.Error);
    }

    /// <summary>Success: <c>201 Created</c> with a <c>Location</c> header built from the value. Failure: ProblemDetails.</summary>
    public static IResult ToCreatedHttpResult<T>(this Result<T> result, Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : new ErrorHttpResult(result.Error);
    }

    /// <summary>ProblemDetails for the error, with the status mapped from <see cref="Error.Type"/>.</summary>
    public static IResult ToHttpResult(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ErrorHttpResult(error);
    }
}
