using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.DependencyInjection;
using MR = PulseFlow.Benchmarks.MediatRSide;
using MRMediator = MediatR.IMediator;
using MS = PulseFlow.Benchmarks.MediatorSide;
using MSMediator = Mediator.IMediator;
using PF = PulseFlow.Benchmarks.PulseFlowSide;
using PFMediator = PulseFlow.Application.Mediator.IMediator;

namespace PulseFlow.Benchmarks;

/// <summary>
/// Registration + container build + first request, measured two ways:
/// <list type="bullet">
/// <item><b>ColdStart</b>: a brand-new process per measurement (20 launches, 1 call each). Includes JIT, type loading,
/// assembly scanning and static initialization: what an app or a serverless function really pays when it starts.</item>
/// <item><b>Warm</b>: repeated in the same process (JIT and reflection caches already populated). This is the cost of
/// building one more container, e.g. per test or per tenant; it is not a cold start.</item>
/// </list>
/// Reflection-based libraries (MediatR, PulseFlow) scan assemblies at startup; Mediator does that work at compile time.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[SimpleJob(RunStrategy.ColdStart, launchCount: 20, warmupCount: 0, iterationCount: 1, id: "ColdStart")]
[SimpleJob(id: "Warm")]
public class StartupBenchmarks
{
    private const string Startup = "5. Startup (register + build + first Send)";

    [BenchmarkCategory(Startup), Benchmark(Baseline = true, Description = "MediatR")]
    public async Task<int> Startup_MediatR()
    {
        using var provider = MR.MediatRSetup.Build(withBehaviors: false);
        return await provider.GetRequiredService<MRMediator>().Send(new MR.Ping(1));
    }

    [BenchmarkCategory(Startup), Benchmark(Description = "Mediator (source gen)")]
    public async Task<int> Startup_Mediator()
    {
        using var provider = MS.MediatorSetup.Build(withBehaviors: false);
        return await provider.GetRequiredService<MSMediator>().Send(new MS.Ping(1));
    }

    [BenchmarkCategory(Startup), Benchmark(Description = "PulseFlow")]
    public async Task<int> Startup_PulseFlow()
    {
        using var provider = PF.PulseFlowSetup.Build(withBehaviors: false, singleton: false);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<PFMediator>().Send(new PF.Ping(1));
        return result.Value;
    }
}
