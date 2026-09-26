using PulseFlow.Application.Queries;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// A behavior that runs only for queries (<see cref="IQuery{TResponse}"/>), never for commands.
/// Typical uses: caching, read-replica routing, query timeouts.
/// Register with <c>services.AddQueryBehavior(typeof(MyBehavior&lt;,&gt;))</c>.
/// </summary>
public interface IQueryPipelineBehavior<in TQuery, TResponse> : IPipelineBehavior<TQuery, TResponse>
    where TQuery : IBaseQuery
{
}
