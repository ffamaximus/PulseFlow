namespace PulseFlow.Domain;

public interface IDomainEventHandler<in TEvent>
    where TEvent : DomainEvent
{
    ValueTask Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
