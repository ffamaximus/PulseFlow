using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// Sends commands and queries (<see cref="ISender"/>) and publishes notifications (<see cref="IPublisher"/>).
/// </summary>
/// <remarks>
/// The members are re-declared here (instead of only being inherited) so code compiled against PulseFlow 2.0 keeps
/// binding to <c>IMediator.Send</c> / <c>IMediator.Publish</c> without recompiling.
/// </remarks>
public interface IMediator : ISender, IPublisher
{
    /// <inheritdoc cref="ISender.Send(ICommand, CancellationToken)"/>
    new ValueTask<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ISender.Send{TResponse}(ICommand{TResponse}, CancellationToken)"/>
    new ValueTask<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ISender.Send{TResponse}(IQuery{TResponse}, CancellationToken)"/>
    new ValueTask<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ISender.CreateStream{TResponse}(IStreamQuery{TResponse}, CancellationToken)"/>
    new IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IPublisher.Publish{TNotification}(TNotification, CancellationToken)"/>
    new ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
