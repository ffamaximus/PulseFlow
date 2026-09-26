namespace PulseFlow.Application.Mediator;

/// <summary>Invokes the next behavior in the pipeline, or the handler when it is the last one.</summary>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting behavior that wraps every command and query handler.
/// <typeparamref name="TResponse"/> is always <see cref="Result"/> or <see cref="Result{T}"/>; constrain it with
/// <c>where TResponse : Result</c> to inspect the outcome, or with
/// <c>where TResponse : IFailureFactory&lt;TResponse&gt;</c> to short-circuit with a failure.
/// </summary>
/// <remarks>
/// Execution order: all <see cref="IPipelineBehavior{TRequest,TResponse}"/> (outermost, in registration order), then the
/// kind-specific <see cref="ICommandPipelineBehavior{TCommand,TResponse}"/> or
/// <see cref="IQueryPipelineBehavior{TQuery,TResponse}"/>, then the handler.
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
