using BenchmarkDotNet.Running;

// Examples:
//   dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks                       (interactive menu)
//   dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*"       (everything)
//   dotnet run -c Release -f net10.0 --project benchmarks/PulseFlow.Benchmarks -- --filter "*Send*"  (one group)
//   ... -- --filter "*" --runtimes net8.0 net10.0                                                     (compare runtimes)
//   ... -- --filter "*" --job Dry                                                                     (smoke test, seconds)
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
