using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Tests;

public class ProcessorAndStreamTests
{
    private static IMediator Build(Probe probe, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediator(typeof(ProcessorAndStreamTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Processors_run_inside_behaviors_around_the_handler()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s
            .AddPipelineBehavior(typeof(GeneralBehavior<,>))
            .AddRequestPreProcessor(typeof(RecordingPreProcessor<>))
            .AddRequestPostProcessor(typeof(RecordingPostProcessor<,>)));

        await mediator.Send(new CreateOrder("book"));

        Assert.Equal(
            new[] { "general:CreateOrder", "pre:CreateOrder", "handler:CreateOrder", "post:CreateOrder:ok" },
            probe.Calls.ToArray());
    }

    [Fact]
    public async Task Post_processors_see_failed_results()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s.AddRequestPostProcessor(typeof(RecordingPostProcessor<,>)));

        await mediator.Send(new CreateOrder("duplicate"));

        Assert.Contains("post:CreateOrder:Conflict", probe.Calls);
    }

    [Fact]
    public async Task Closed_processors_are_discovered_by_scanning()
    {
        var probe = new Probe();
        var mediator = Build(probe);

        await mediator.Send(new GetThing(7));

        Assert.Equal(new[] { "scanned-pre:7", "handler:GetThing" }, probe.Calls.ToArray());
    }

    [Fact]
    public async Task Stream_behaviors_wrap_the_stream()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s.AddStreamBehavior(typeof(TakeTwoStreamBehavior<,>)));

        var items = new List<int>();
        await foreach (var i in mediator.CreateStream(new CountTo(5)))
            items.Add(i);

        Assert.Equal(new[] { 1, 2 }, items.ToArray());
        Assert.Contains("stream:CountTo", probe.Calls);
    }

    [Fact]
    public void Wrong_processor_arity_fails_fast()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddRequestPreProcessor(typeof(RecordingPostProcessor<,>)));
        Assert.Throws<ArgumentException>(() => services.AddStreamBehavior(typeof(GeneralBehavior<,>)));
    }

    [Fact]
    public void Lifetimes_are_configurable()
    {
        var services = new ServiceCollection();
        services.AddMediator(o =>
        {
            o.HandlerLifetime = ServiceLifetime.Scoped;
            o.MediatorLifetime = ServiceLifetime.Transient;
        }, typeof(ProcessorAndStreamTests).Assembly);

        Assert.Equal(ServiceLifetime.Transient, services.Single(d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(ICommandHandler<CreateThing>)).Lifetime);
    }

    [Fact]
    public void Default_lifetimes_are_unchanged()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProcessorAndStreamTests).Assembly);

        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, services.Single(d => d.ServiceType == typeof(ICommandHandler<CreateThing>)).Lifetime);
    }

    [Fact]
    public async Task Empty_pipeline_cache_is_per_container()
    {
        // Container A has no behaviors; container B has one. The "no behaviors" knowledge of A must not leak into B.
        var probeA = new Probe();
        var probeB = new Probe();
        var withoutBehaviors = Build(probeA);
        var withBehavior = Build(probeB, s => s.AddPipelineBehavior(typeof(GeneralBehavior<,>)));

        await withoutBehaviors.Send(new CreateThing("a"));
        await withoutBehaviors.Send(new CreateThing("a"));
        await withBehavior.Send(new CreateThing("b"));

        Assert.DoesNotContain("general:CreateThing", probeA.Calls);
        Assert.Contains("general:CreateThing", probeB.Calls);
    }

    [Fact]
    public async Task Repeated_sends_keep_running_behaviors()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s.AddPipelineBehavior(typeof(GeneralBehavior<,>)));

        await mediator.Send(new CreateThing("a"));
        await mediator.Send(new CreateThing("b"));

        Assert.Equal(2, probe.Calls.Count(c => c == "general:CreateThing"));
    }
}
