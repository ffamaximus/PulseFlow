namespace PulseFlow.Application.Queries;

/// <summary>
/// Marker shared by every query. Use it as a generic constraint
/// (for example in an <see cref="Mediator.IQueryPipelineBehavior{TQuery,TResponse}"/>).
/// </summary>
public interface IBaseQuery { }

/// <summary>A query that reads state without changing it and returns <see cref="Result{T}"/>.</summary>
public interface IQuery<TResponse> : IBaseQuery { }
