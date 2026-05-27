namespace PulseFlow.Domain;

public interface IDomainEventHandler<in TEvent>
    where TEvent : DomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default);
}
