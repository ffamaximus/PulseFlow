using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;

namespace PulseFlow.Application.Mediator;

public interface IMediator
{
    /// <summary>Sends a command without response through the pipeline to its handler.</summary>
    ValueTask<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command with response through the pipeline to its handler.</summary>
    ValueTask<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>Sends a query through the pipeline to its handler.</summary>
    ValueTask<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>Creates the stream produced by the handler of a stream query.</summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>Publishes a notification to all its handlers using the configured <see cref="PublishStrategy"/>.</summary>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
