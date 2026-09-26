namespace PulseFlow.Application.Mediator;

public interface INotification { }

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    ValueTask Handle(TNotification notification, CancellationToken cancellationToken);
}
