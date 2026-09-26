using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using MR = PulseFlow.Benchmarks.MediatRSide;
using MRMediator = MediatR.IMediator;
using MS = PulseFlow.Benchmarks.MediatorSide;
using MSMediator = Mediator.IMediator;
using PF = PulseFlow.Benchmarks.PulseFlowSide;
using PFMediator = PulseFlow.Application.Mediator.IMediator;
using PFResult = PulseFlow.Application.Result<int>;

namespace PulseFlow.Benchmarks;

/// <summary>
/// Steady-state dispatch cost (warm containers, handlers that do no work), so the numbers measure the mediator itself.
/// Each group compares the three libraries on the same workload; Ratio is relative to MediatR (the baseline).
/// Every contestant runs with its default configuration: MediatR (transient handlers), Mediator (singletons, source
/// generated) and PulseFlow (scoped mediator, transient handlers). "PulseFlow (singleton)" shows PulseFlow configured
/// like Mediator.
/// </summary>
[Config(typeof(BenchmarkConfig))]
public class DispatchBenchmarks
{
    private const string Send = "1. Send";
    private const string SendWithBehaviors = "2. Send + 2 behaviors";
    private const string Publish = "3. Publish to 2 handlers";
    private const string Stream = "4. Stream 10 items";
    private const int StreamItems = 10;

    private readonly List<IDisposable> _disposables = [];

    private readonly PF.Ping _pfPing = new(1);
    private readonly PF.Pinged _pfPinged = new(1);
    private readonly PF.CountTo _pfCount = new(StreamItems);
    private readonly PF.PingHandler _pfHandler = new();
    private readonly MS.Ping _msPing = new(1);
    private readonly MS.Pinged _msPinged = new(1);
    private readonly MS.CountTo _msCount = new(StreamItems);
    private readonly MR.Ping _mrPing = new(1);
    private readonly MR.Pinged _mrPinged = new(1);
    private readonly MR.CountTo _mrCount = new(StreamItems);

    private PFMediator _pulseFlow = null!;
    private PFMediator _pulseFlowSingleton = null!;
    private PFMediator _pulseFlowWithBehaviors = null!;
    private PFMediator _pulseFlowSingletonWithBehaviors = null!;
    private MSMediator _mediator = null!;
    private MSMediator _mediatorWithBehaviors = null!;
    private MRMediator _mediatR = null!;
    private MRMediator _mediatRWithBehaviors = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pulseFlow = Resolve<PFMediator>(PF.PulseFlowSetup.Build(withBehaviors: false, singleton: false));
        _pulseFlowSingleton = Resolve<PFMediator>(PF.PulseFlowSetup.Build(withBehaviors: false, singleton: true));
        _pulseFlowWithBehaviors = Resolve<PFMediator>(PF.PulseFlowSetup.Build(withBehaviors: true, singleton: false));
        _pulseFlowSingletonWithBehaviors = Resolve<PFMediator>(PF.PulseFlowSetup.Build(withBehaviors: true, singleton: true));
        _mediator = Resolve<MSMediator>(MS.MediatorSetup.Build(withBehaviors: false));
        _mediatorWithBehaviors = Resolve<MSMediator>(MS.MediatorSetup.Build(withBehaviors: true));
        _mediatR = Resolve<MRMediator>(MR.MediatRSetup.Build(withBehaviors: false));
        _mediatRWithBehaviors = Resolve<MRMediator>(MR.MediatRSetup.Build(withBehaviors: true));
    }

    // Like a web request: one scope, reused for every call of the benchmark.
    private T Resolve<T>(ServiceProvider provider) where T : notnull
    {
        var scope = provider.CreateScope();
        _disposables.Add(scope);
        _disposables.Add(provider);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        _disposables.Clear();
    }

    // ---------------------------------------------------------------- 1. Send

    [BenchmarkCategory(Send), Benchmark(Description = "Direct handler call (no mediator)")]
    public ValueTask<PFResult> Send_Direct() => _pfHandler.Handle(_pfPing, CancellationToken.None);

    [BenchmarkCategory(Send), Benchmark(Baseline = true, Description = "MediatR")]
    public Task<int> Send_MediatR() => _mediatR.Send(_mrPing);

    [BenchmarkCategory(Send), Benchmark(Description = "Mediator (source gen)")]
    public ValueTask<int> Send_Mediator() => _mediator.Send(_msPing);

    [BenchmarkCategory(Send), Benchmark(Description = "PulseFlow")]
    public ValueTask<PFResult> Send_PulseFlow() => _pulseFlow.Send(_pfPing);

    [BenchmarkCategory(Send), Benchmark(Description = "PulseFlow (singleton)")]
    public ValueTask<PFResult> Send_PulseFlowSingleton() => _pulseFlowSingleton.Send(_pfPing);

    // ---------------------------------------------------------------- 2. Send + behaviors

    [BenchmarkCategory(SendWithBehaviors), Benchmark(Baseline = true, Description = "MediatR")]
    public Task<int> Behaviors_MediatR() => _mediatRWithBehaviors.Send(_mrPing);

    [BenchmarkCategory(SendWithBehaviors), Benchmark(Description = "Mediator (source gen)")]
    public ValueTask<int> Behaviors_Mediator() => _mediatorWithBehaviors.Send(_msPing);

    [BenchmarkCategory(SendWithBehaviors), Benchmark(Description = "PulseFlow")]
    public ValueTask<PFResult> Behaviors_PulseFlow() => _pulseFlowWithBehaviors.Send(_pfPing);

    [BenchmarkCategory(SendWithBehaviors), Benchmark(Description = "PulseFlow (singleton)")]
    public ValueTask<PFResult> Behaviors_PulseFlowSingleton() => _pulseFlowSingletonWithBehaviors.Send(_pfPing);

    // ---------------------------------------------------------------- 3. Publish

    [BenchmarkCategory(Publish), Benchmark(Baseline = true, Description = "MediatR")]
    public Task Publish_MediatR() => _mediatR.Publish(_mrPinged);

    [BenchmarkCategory(Publish), Benchmark(Description = "Mediator (source gen)")]
    public ValueTask Publish_Mediator() => _mediator.Publish(_msPinged);

    [BenchmarkCategory(Publish), Benchmark(Description = "PulseFlow")]
    public ValueTask Publish_PulseFlow() => _pulseFlow.Publish(_pfPinged);

    // ---------------------------------------------------------------- 4. Stream

    [BenchmarkCategory(Stream), Benchmark(Baseline = true, Description = "MediatR")]
    public async Task<int> Stream_MediatR()
    {
        var sum = 0;
        await foreach (var item in _mediatR.CreateStream(_mrCount))
            sum += item;
        return sum;
    }

    [BenchmarkCategory(Stream), Benchmark(Description = "Mediator (source gen)")]
    public async Task<int> Stream_Mediator()
    {
        var sum = 0;
        await foreach (var item in _mediator.CreateStream(_msCount))
            sum += item;
        return sum;
    }

    [BenchmarkCategory(Stream), Benchmark(Description = "PulseFlow")]
    public async Task<int> Stream_PulseFlow()
    {
        var sum = 0;
        await foreach (var item in _pulseFlow.CreateStream(_pfCount))
            sum += item;
        return sum;
    }
}
