namespace PulseFlow.Application.Queries;

public interface IStreamQueryHandler<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}
