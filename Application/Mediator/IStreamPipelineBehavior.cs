namespace PulseFlow.Application.Mediator;

/// <summary>Invokes the next stream behavior in the pipeline, or the stream handler when it is the last one.</summary>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<out TResponse>();

/// <summary>
/// Cross-cutting behavior for stream queries (<see cref="Queries.IStreamQuery{TResponse}"/>): it can observe, filter,
/// transform or stop the items produced by the handler. Register with
/// <c>services.AddStreamBehavior(typeof(MyStreamBehavior&lt;,&gt;))</c>; behaviors run in registration order.
/// </summary>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
