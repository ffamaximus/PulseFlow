using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace PulseFlow.Application.Mediator;

/// <summary>Traces and metrics for the mediator. Only used when a listener is attached (see <see cref="IsEnabled"/>).</summary>
internal static class MediatorTelemetry
{
    public const string Command = "command";
    public const string Query = "query";
    public const string Notification = "notification";
    public const string Stream = "stream";

    private const string KindTag = "pulseflow.request.kind";
    private const string TypeTag = "pulseflow.request.type";
    private const string OutcomeTag = "pulseflow.outcome";
    private const string ErrorTypeTag = "error.type";

    private static readonly string? Version = typeof(MediatorTelemetry).Assembly.GetName().Version?.ToString();

    private static readonly ActivitySource Source = new(PulseFlowDiagnostics.ActivitySourceName, Version);
    private static readonly Meter Meter = new(PulseFlowDiagnostics.MeterName, Version);

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        PulseFlowDiagnostics.RequestDurationMetric,
        unit: "s",
        description: "Duration of PulseFlow commands, queries, notifications and streams.");

    /// <summary>False when nobody listens: the mediator then takes its uninstrumented fast path.</summary>
    public static bool IsEnabled => Source.HasListeners() || Duration.Enabled;

    public static async ValueTask<TResult> Track<TResult>(string kind, Type requestType, Func<ValueTask<TResult>> run)
        where TResult : Result
    {
        using var activity = StartActivity(kind, requestType);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var result = await run().ConfigureAwait(false);
            if (result.IsSuccess)
            {
                Complete(activity, kind, requestType, start, "success", null);
            }
            else
            {
                activity?.SetTag("pulseflow.error.code", result.Error.Code);
                Complete(activity, kind, requestType, start, "failure", result.Error.Type.ToString());
            }

            return result;
        }
        catch (Exception ex)
        {
            Fail(activity, kind, requestType, start, ex);
            throw;
        }
    }

    public static async ValueTask TrackNotification(Type notificationType, Func<ValueTask> run)
    {
        const string kind = Notification;
        var requestType = notificationType;
        using var activity = StartActivity(kind, requestType);
        var start = Stopwatch.GetTimestamp();
        try
        {
            await run().ConfigureAwait(false);
            Complete(activity, kind, requestType, start, "success", null);
        }
        catch (Exception ex)
        {
            Fail(activity, kind, requestType, start, ex);
            throw;
        }
    }

    public static async IAsyncEnumerable<T> TrackStream<T>(
        Type requestType,
        Func<IAsyncEnumerable<T>> create,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity(Stream, requestType);
        var start = Stopwatch.GetTimestamp();
        var items = 0;
        var completed = false;
        try
        {
            await foreach (var item in create().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                items++;
                yield return item;
            }

            completed = true;
        }
        finally
        {
            activity?.SetTag("pulseflow.stream.items", items);
            // "incomplete": the handler threw, the consumer stopped early, or the enumeration was cancelled.
            Complete(activity, Stream, requestType, start, completed ? "success" : "incomplete", null);
        }
    }

    private static Activity? StartActivity(string kind, Type requestType)
    {
        if (!Source.HasListeners())
            return null;

        var activity = Source.StartActivity($"{kind} {requestType.Name}", ActivityKind.Internal);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(KindTag, kind);
            activity.SetTag(TypeTag, requestType.FullName ?? requestType.Name);
        }

        return activity;
    }

    private static void Fail(Activity? activity, string kind, Type requestType, long start, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("exception.type", exception.GetType().FullName);
        activity?.SetTag("exception.message", exception.Message);
        Complete(activity, kind, requestType, start, "exception", exception.GetType().FullName);
    }

    private static void Complete(Activity? activity, string kind, Type requestType, long start, string outcome, string? errorType)
    {
        activity?.SetTag(OutcomeTag, outcome);
        if (errorType is not null)
            activity?.SetTag(ErrorTypeTag, errorType);

        if (!Duration.Enabled)
            return;

        var tags = new TagList
        {
            { KindTag, kind },
            { TypeTag, requestType.Name },
            { OutcomeTag, outcome }
        };
        if (errorType is not null)
            tags.Add(ErrorTypeTag, errorType);

        Duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
    }
}
