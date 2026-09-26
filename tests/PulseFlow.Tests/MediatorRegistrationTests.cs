using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Mediator;
using PulseFlow.Domain;

namespace PulseFlow.Tests;

public class MediatorRegistrationTests
{
    private static ServiceProvider BuildScanned(Probe probe, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediator(typeof(MediatorRegistrationTests).Assembly);
        extra?.Invoke(services);

        // ValidateOnBuild proves every scanned registration can actually be constructed.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task Commands_and_queries_are_dispatched()
    {
        var probe = new Probe();
        using var root = BuildScanned(probe);
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var command = await mediator.Send(new CreateThing("a"));
        var query = await mediator.Send(new GetThing(5));

        Assert.True(command.IsSuccess);
        Assert.Equal("thing-5", query.Value);
        Assert.Contains("create:a", probe.Calls);
    }

    [Fact]
    public async Task Notification_handlers_are_discovered_by_scanning()
    {
        var probe = new Probe();
        using var root = BuildScanned(probe);
        using var scope = root.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new ThingCreated("a"));

        Assert.Equal(new[] { "audit", "email" }, probe.Calls.OrderBy(c => c).ToArray());
    }

    [Fact]
    public async Task Stream_handlers_are_discovered_by_scanning()
    {
        using var root = BuildScanned(new Probe());
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var i in mediator.CreateStream(new CountTo(3)))
            items.Add(i);

        Assert.Equal(new[] { 1, 2, 3 }, items.ToArray());
    }

    [Fact]
    public void Open_generic_handlers_are_skipped_instead_of_breaking_the_container()
    {
        using var root = BuildScanned(new Probe()); // would throw on build before the fix
        using var scope = root.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ICommandHandler<GenericCommand<int>>>());
    }

    [Fact]
    public async Task Calling_AddMediator_twice_does_not_duplicate_registrations()
    {
        var probe = new Probe();
        using var root = BuildScanned(probe, s => s.AddMediator(typeof(MediatorRegistrationTests).Assembly));
        using var scope = root.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IMediator>());

        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new ThingCreated("a"));
        Assert.Equal(2, probe.Calls.Count);
    }

    [Fact]
    public void Options_are_configurable_and_shared_across_calls()
    {
        var services = new ServiceCollection();
        services.AddMediator(o => o.PublishStrategy = PublishStrategy.Parallel, typeof(MediatorRegistrationTests).Assembly);
        services.AddMediator(typeof(MediatorRegistrationTests).Assembly);

        using var root = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(MediatorOptions));
        Assert.Equal(PublishStrategy.Parallel, root.GetRequiredService<MediatorOptions>().PublishStrategy);
    }

    [Fact]
    public async Task Domain_event_dispatcher_is_registered_and_finds_internal_handlers()
    {
        var probe = new Probe();
        using var root = BuildScanned(probe);
        using var scope = root.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var order = new Order(Guid.NewGuid());
        order.Place();
        await dispatcher.DispatchAsync(order.DomainEvents);

        Assert.Contains($"order:{order.Id}", probe.Calls);
    }
}
