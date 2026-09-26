namespace PulseFlow.Application.Mediator;

/// <summary>
/// Names of the OpenTelemetry sources emitted by PulseFlow. Enable them in your app:
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(t =&gt; t.AddSource(PulseFlowDiagnostics.ActivitySourceName))
///     .WithMetrics(m =&gt; m.AddMeter(PulseFlowDiagnostics.MeterName));
/// </code>
/// Nothing is recorded (and the dispatch path is not changed) until a listener subscribes.
/// </summary>
public static class PulseFlowDiagnostics
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/>: one span per command, query, notification and stream.</summary>
    public const string ActivitySourceName = "PulseFlow";

    /// <summary>Name of the <see cref="System.Diagnostics.Metrics.Meter"/>.</summary>
    public const string MeterName = "PulseFlow";

    /// <summary>
    /// Histogram (seconds) of request duration. Tags: <c>pulseflow.request.kind</c> (command, query, notification, stream),
    /// <c>pulseflow.request.type</c>, <c>pulseflow.outcome</c> (success, failure, exception, incomplete) and
    /// <c>error.type</c> (the <c>ErrorType</c> of a failed result, or the exception type).
    /// </summary>
    public const string RequestDurationMetric = "pulseflow.request.duration";
}
