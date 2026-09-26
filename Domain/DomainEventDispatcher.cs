using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Domain;

/// <summary>
/// Dispatches domain events sequentially, in the order they were raised, using cached typed wrappers
/// (no <c>dynamic</c>). Works with <c>internal</c> handlers and with explicit interface implementations.
/// </summary>
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, DomainEventWrapperBase> Wrappers = new();

    private readonly IServiceProvider _provider;

    public DomainEventDispatcher(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task DispatchAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        // Snapshot: handlers may raise new events or call ClearEvents() on the same entity while we iterate.
        foreach (var domainEvent in events.ToArray())
        {
            if (domainEvent is null)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            var wrapper = Wrappers.GetOrAdd(domainEvent.GetType(), static t =>
                (DomainEventWrapperBase)Activator.CreateInstance(typeof(DomainEventWrapper<>).MakeGenericType(t))!);

            await wrapper.Handle(domainEvent, _provider, cancellationToken).ConfigureAwait(false);
        }
    }

    private abstract class DomainEventWrapperBase
    {
        public abstract Task Handle(DomainEvent domainEvent, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class DomainEventWrapper<TEvent> : DomainEventWrapperBase where TEvent : DomainEvent
    {
        public override async Task Handle(DomainEvent domainEvent, IServiceProvider provider, CancellationToken ct)
        {
            var typed = (TEvent)domainEvent;
            foreach (var handler in provider.GetServices<IDomainEventHandler<TEvent>>())
                await handler.Handle(typed, ct).ConfigureAwait(false);
        }
    }
}
