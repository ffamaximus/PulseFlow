using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Mediator;
using PulseFlow.Application.Queries;

namespace PulseFlow.AspNetCore;

/// <summary>
/// Maps commands and queries to minimal API endpoints. Each method returns the <see cref="RouteHandlerBuilder"/>, so
/// <c>.WithName()</c>, <c>.RequireAuthorization()</c>, <c>.WithTags()</c>, etc. can be chained.
/// </summary>
/// <remarks>
/// Binding: POST, PUT and PATCH read the command from the JSON body; GET and DELETE bind it from the route and query
/// string (<c>[AsParameters]</c>). For requests that mix route values and a body, write the endpoint by hand and use
/// <see cref="ResultHttpExtensions.ToHttpResult(PulseFlow.Application.Result)"/>.
/// </remarks>
public static class PulseFlowEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a command without response. Success: <c>204 No Content</c>. Failure: ProblemDetails.
    /// </summary>
    public static RouteHandlerBuilder MapCommand<TCommand>(this IEndpointRouteBuilder endpoints, string pattern, string httpMethod = "POST")
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var builder = ReadsBody(httpMethod)
            ? endpoints.MapMethods(pattern, [httpMethod],
                async (TCommand command, IMediator mediator, CancellationToken ct)
                    => (await mediator.Send(command, ct)).ToHttpResult())
            : endpoints.MapMethods(pattern, [httpMethod],
                async ([AsParameters] TCommand command, IMediator mediator, CancellationToken ct)
                    => (await mediator.Send(command, ct)).ToHttpResult());

        return builder
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Maps a command with response. Success: <c>200 OK</c> with the value, or <c>201 Created</c> with a
    /// <c>Location</c> header when <paramref name="createdLocation"/> is given. Failure: ProblemDetails.
    /// </summary>
    public static RouteHandlerBuilder MapCommand<TCommand, TResponse>(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Func<TResponse, string>? createdLocation = null,
        string httpMethod = "POST")
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var builder = ReadsBody(httpMethod)
            ? endpoints.MapMethods(pattern, [httpMethod],
                async (TCommand command, IMediator mediator, CancellationToken ct)
                    => ToHttpResult(await mediator.Send<TResponse>(command, ct), createdLocation))
            : endpoints.MapMethods(pattern, [httpMethod],
                async ([AsParameters] TCommand command, IMediator mediator, CancellationToken ct)
                    => ToHttpResult(await mediator.Send<TResponse>(command, ct), createdLocation));

        return builder
            .Produces<TResponse>(createdLocation is null ? StatusCodes.Status200OK : StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Maps a query to a GET endpoint, binding it from the route and query string. Success: <c>200 OK</c> with the
    /// value. Failure: ProblemDetails.
    /// </summary>
    public static RouteHandlerBuilder MapQuery<TQuery, TResponse>(this IEndpointRouteBuilder endpoints, string pattern)
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapGet(pattern, async ([AsParameters] TQuery query, IMediator mediator, CancellationToken ct)
                => (await mediator.Send<TResponse>(query, ct)).ToHttpResult())
            .Produces<TResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult ToHttpResult<TResponse>(PulseFlow.Application.Result<TResponse> result, Func<TResponse, string>? createdLocation)
        => createdLocation is null ? result.ToHttpResult() : result.ToCreatedHttpResult(createdLocation);

    private static bool ReadsBody(string httpMethod)
        => HttpMethods.IsPost(httpMethod) || HttpMethods.IsPut(httpMethod) || HttpMethods.IsPatch(httpMethod);
}
