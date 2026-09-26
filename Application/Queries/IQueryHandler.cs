namespace PulseFlow.Application.Queries;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    ValueTask<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
