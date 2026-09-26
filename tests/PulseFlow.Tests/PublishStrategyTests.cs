using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Tests;

public class PublishStrategyTests
{
    // Manual registration keeps the handler order under control.
    private static IMediator Build(PublishStrategy? strategy, Probe probe, params Type[] pingHandlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        if (strategy is not null)
            services.AddSingleton(new MediatorOptions { PublishStrategy = strategy.Value });
        services.AddScoped<IMediator, Mediator>();
        foreach (var handler in pingHandlers)
            services.AddTransient(typeof(INotificationHandler<Ping>), handler);

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public void Default_strategy_is_sequential()
        => Assert.Equal(PublishStrategy.Sequential, new MediatorOptions().PublishStrategy);

    [Fact]
    public async Task Mediator_without_registered_options_publishes_sequentially()
    {
        var probe = new Probe();
        var mediator = Build(null, probe, typeof(ConcurrencyTrackingHandler), typeof(ConcurrencyTrackingHandler));

        await mediator.Publish(new Ping());

        Assert.Equal(1, probe.MaxConcurrency);
    }

    [Fact]
    public async Task Sequential_runs_handlers_in_order_and_never_concurrently()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.Sequential, probe,
            typeof(FirstRecordingHandler), typeof(ConcurrencyTrackingHandler), typeof(ConcurrencyTrackingHandler), typeof(SecondRecordingHandler));

        await mediator.Publish(new Ping());

        Assert.Equal(new[] { "first", "second" }, probe.Calls.ToArray());
        Assert.Equal(1, probe.MaxConcurrency);
    }

    [Fact]
    public async Task Sequential_runs_every_handler_and_rethrows_a_single_failure_as_is()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.Sequential, probe, typeof(ThrowingAsyncHandler), typeof(FirstRecordingHandler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new Ping()).AsTask());

        Assert.Equal("async failure", ex.Message);
        Assert.Equal(new[] { "first" }, probe.Calls.ToArray());
    }

    [Fact]
    public async Task Sequential_aggregates_multiple_failures()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.Sequential, probe,
            typeof(ThrowingAsyncHandler), typeof(ThrowingSyncHandler), typeof(FirstRecordingHandler));

        var ex = await Assert.ThrowsAsync<AggregateException>(() => mediator.Publish(new Ping()).AsTask());

        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.Equal(new[] { "first" }, probe.Calls.ToArray());
    }

    [Fact]
    public async Task StopOnException_stops_at_the_first_failure()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.StopOnException, probe, typeof(ThrowingAsyncHandler), typeof(FirstRecordingHandler));

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new Ping()).AsTask());

        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task Parallel_runs_handlers_concurrently()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.Parallel, probe, typeof(ConcurrencyTrackingHandler), typeof(ConcurrencyTrackingHandler));

        await mediator.Publish(new Ping());

        Assert.Equal(2, probe.MaxConcurrency);
    }

    [Fact]
    public async Task Parallel_synchronous_throw_does_not_prevent_other_handlers()
    {
        var probe = new Probe();
        var mediator = Build(PublishStrategy.Parallel, probe, typeof(ThrowingSyncHandler), typeof(FirstRecordingHandler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Publish(new Ping()).AsTask());

        Assert.Equal("sync failure", ex.Message);
        Assert.Equal(new[] { "first" }, probe.Calls.ToArray());
    }

    [Fact]
    public async Task Publish_without_handlers_is_a_no_op()
    {
        var mediator = Build(PublishStrategy.Sequential, new Probe());
        await mediator.Publish(new Ping());
    }
}
