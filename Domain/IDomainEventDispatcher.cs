namespace PulseFlow.Domain;

/// <summary>
/// Dispatches domain events to their registered <see cref="IDomainEventHandler{TEvent}"/> implementations.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default);
}
