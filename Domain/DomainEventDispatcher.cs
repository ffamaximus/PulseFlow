using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Domain;

public class DomainEventDispatcher
{
    private readonly IServiceProvider _provider;

    public DomainEventDispatcher(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task DispatchAsync(IEnumerable<DomainEvent> events)
    {
        foreach (var domainEvent in events)
        {
            var handlers = _provider.GetServices(typeof(IDomainEventHandler<>)
                .MakeGenericType(domainEvent.GetType())).Cast<dynamic>();

            foreach (var handler in handlers)
                await handler.Handle((dynamic)domainEvent);
        }
    }
}
