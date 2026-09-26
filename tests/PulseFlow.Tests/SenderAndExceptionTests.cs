using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Tests;

public class SenderAndExceptionTests
{
    private static IServiceProvider BuildScope(Probe probe, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediator(typeof(SenderAndExceptionTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true })
            .CreateScope().ServiceProvider;
    }

    [Fact]
    public async Task ISender_and_IPublisher_resolve_to_the_same_mediator()
    {
        var probe = new Probe();
        var scope = BuildScope(probe);

        var mediator = scope.GetRequiredService<IMediator>();
        var sender = scope.GetRequiredService<ISender>();
        var publisher = scope.GetRequiredService<IPublisher>();

        Assert.Same(mediator, sender);
        Assert.Same(mediator, publisher);

        Assert.True((await sender.Send(new CreateOrder("book"))).IsSuccess);
        await publisher.Publish(new ThingCreated("a"));
        Assert.Contains("audit", probe.Calls);
    }

    [Fact]
    public void ISender_follows_the_mediator_lifetime()
    {
        var services = new ServiceCollection();
        services.AddMediator(o => o.MediatorLifetime = ServiceLifetime.Transient, typeof(SenderAndExceptionTests).Assembly);

        Assert.Equal(ServiceLifetime.Transient, services.Single(d => d.ServiceType == typeof(ISender)).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, services.Single(d => d.ServiceType == typeof(IPublisher)).Lifetime);
    }

    [Fact]
    public async Task Exception_handler_turns_an_exception_into_a_typed_failure()
    {
        var probe = new Probe();
        var sender = BuildScope(probe, s => s.AddRequestExceptionHandler(typeof(ConflictOnInvalidOperation<,>)))
            .GetRequiredService<ISender>();

        var result = await sender.Send(new ExplodingCommand("invalid"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("State.Invalid", result.Error.Code);
        Assert.Contains("exception-handler:InvalidOperationException", probe.Calls);
    }

    [Fact]
    public async Task Unhandled_exceptions_are_rethrown()
    {
        var probe = new Probe();
        var sender = BuildScope(probe, s => s.AddRequestExceptionHandler(typeof(ConflictOnInvalidOperation<,>)))
            .GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ArgumentException>(() => sender.Send(new ExplodingCommand("argument")).AsTask());
        Assert.Contains("exception-handler:ArgumentException", probe.Calls);
    }

    [Fact]
    public async Task Cancellation_is_never_passed_to_exception_handlers()
    {
        var probe = new Probe();
        var sender = BuildScope(probe, s => s.AddRequestExceptionHandler(typeof(ConflictOnInvalidOperation<,>)))
            .GetRequiredService<ISender>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.Send(new ExplodingCommand("ok"), cts.Token).AsTask());
        Assert.DoesNotContain(probe.Calls, c => c.StartsWith("exception-handler"));
    }

    [Fact]
    public async Task Without_exception_handlers_exceptions_propagate_unchanged()
    {
        var sender = BuildScope(new Probe()).GetRequiredService<ISender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new ExplodingCommand("invalid")).AsTask());
    }
}
