using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;

namespace PulseFlow.Benchmarks;

/// <summary>
/// Metrics collected for every benchmark:
/// Mean / Error / StdDev (time per operation), Ratio vs. the MediatR baseline of each group, Rank inside the group,
/// Gen0 collections and Allocated bytes per operation (MemoryDiagnoser) and Alloc Ratio.
/// Results are exported as GitHub markdown, HTML, CSV (BenchmarkDotNet defaults) and JSON under BenchmarkDotNet.Artifacts/results.
/// Groups are per category and per job, so each group has its own MediatR baseline.
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(CategoriesColumn.Default, RankColumn.Arabic);
        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByJob);
        AddExporter(JsonExporter.Brief); // markdown, HTML and CSV are already added by BenchmarkDotNet
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));
    }
}
