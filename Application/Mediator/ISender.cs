using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// Sends commands and queries. Inject it instead of <see cref="IMediator"/> in classes that never publish
/// notifications, so their dependencies say exactly what they do.
/// </summary>
public interface ISender
{
    /// <summary>Sends a command without response through the pipeline to its handler.</summary>
    ValueTask<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command with response through the pipeline to its handler.</summary>
    ValueTask<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>Sends a query through the pipeline to its handler.</summary>
    ValueTask<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>Creates the stream produced by the handler of a stream query.</summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default);
}
