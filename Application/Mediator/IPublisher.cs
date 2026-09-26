namespace PulseFlow.Application.Mediator;

/// <summary>
/// Publishes notifications. Inject it instead of <see cref="IMediator"/> in classes that only publish.
/// </summary>
public interface IPublisher
{
    /// <summary>Publishes a notification to all its handlers using the configured <see cref="PublishStrategy"/>.</summary>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
