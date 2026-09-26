# PulseFlow benchmarks

Compares **PulseFlow**, **MediatR 14** and **Mediator 3 (source generator)** on identical workloads with
[BenchmarkDotNet](https://benchmarkdotnet.org/).

## Run

Always in `Release` (BenchmarkDotNet refuses Debug builds):

```bash
# everything (~10-15 min)
dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*"

# one group
dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*Send*"

# compare .NET 8 and .NET 10
dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*" --runtimes net8.0 net10.0

# smoke test (one iteration each, seconds) - what CI runs
dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*DispatchBenchmarks*" --job Dry
```

Results are written to `BenchmarkDotNet.Artifacts/results/` as GitHub markdown (paste it in the README or a release),
HTML and JSON. On GitHub, *Actions > Benchmarks > Run workflow* runs them and shows the table in the job summary.

For stable numbers: plug the laptop in, close other apps, and do not compare runs made on different machines.

## Scenarios

| Group | What it measures |
|---|---|
| 1. Send | One command with response, no behaviors. Includes a direct handler call as the theoretical floor. |
| 2. Send + 2 behaviors | Same command through two pass-through behaviors: cost of the pipeline. |
| 3. Publish to 2 handlers | One notification, two handlers, sequential publishing (the default of all three). |
| 4. Stream 10 items | Create a stream and consume 10 items. |
| 5. Startup | Registration + container build + first request, in two jobs: **ColdStart** (new process per measurement: JIT, type loading, assembly scanning; what an app or serverless function pays when it starts) and **Warm** (same process, caches populated: the cost of one more container, e.g. per test). |

Handlers do no work, so the numbers are the mediator overhead only. In a real request that overhead is tiny compared
with a database or HTTP call; the benchmarks show *relative* cost and *allocations*.

## Reading the results

| Column | Meaning |
|---|---|
| Mean | Average time per operation (ns = nanoseconds, μs = microseconds). Lower is better. |
| Error / StdDev | Confidence interval and dispersion. If Error is large compared with Mean, the run was noisy: repeat it. |
| Ratio | Time relative to MediatR in the same group (`2.5x faster` / `1.3x slower`). The most useful column. |
| Rank | Position inside the group (1 = fastest). |
| Gen0 | Garbage collections per 1,000 operations. |
| Allocated | Bytes allocated per operation. Fewer allocations = less GC pressure under load. |
| Alloc Ratio | Allocations relative to MediatR. |

## Recorded results

Runs worth keeping are stored in [`results/`](results/) so each version can be compared with the previous ones:

- [2.0.0 baseline](results/2.0.0-baseline.md) (before the 2.1 dispatch optimizations; its startup numbers are warm only).
- [2.1.0-preview.1](results/2.1.0-preview.1.md) (dispatch optimizations, first real cold-start measurement).
- [2.1.0-preview.2](results/2.1.0-preview.2.md) (per-container dispatch plans: send -26%, behaviors -33% to -49%).

## Fairness notes

- Every library runs with **its default configuration**: MediatR (transient handlers), Mediator (singleton handlers,
  source-generated dispatch) and PulseFlow (scoped mediator, transient handlers). *PulseFlow (singleton)* shows PulseFlow
  configured like Mediator, so the lifetime effect is visible.
- All mediators are resolved once from a scope and reused, like inside a web request.
- PulseFlow handlers return `Result<int>`, which is a small heap allocation per call; MediatR and Mediator return a
  plain `int`. That is the cost of typed results, and it is included in the numbers on purpose.
- Mediator is called through `IMediator`. Its generated concrete `Mediator` class is faster still; most applications
  inject the interface.
- The same dependency injection container version is used for everyone.
