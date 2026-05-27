namespace PulseFlow.Application.Mediator;

public interface INotification { }

public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
