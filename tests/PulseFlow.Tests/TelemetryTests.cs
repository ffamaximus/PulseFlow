using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Tests;

public class TelemetryTests
{
    /// <summary>Collects PulseFlow activities and measurements while alive.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public ConcurrentQueue<Activity> Activities { get; } = new();
        public ConcurrentQueue<(double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

        public Capture()
        {
            _activityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PulseFlowDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Enqueue(activity)
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == PulseFlowDiagnostics.MeterName)
                        listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                var copy = new Dictionary<string, object?>();
                foreach (var tag in tags)
                    copy[tag.Key] = tag.Value;
                Measurements.Enqueue((value, copy));
            });
            _meterListener.Start();
        }

        public Activity Single(string displayName, Func<Activity, bool>? predicate = null)
            => Assert.Single(Activities, a => a.DisplayName == displayName && (predicate?.Invoke(a) ?? true));

        public bool HasMeasurement(string type, string outcome, string? errorType = null)
            => Measurements.Any(m =>
                Equals(m.Tags.GetValueOrDefault("pulseflow.request.type"), type)
                && Equals(m.Tags.GetValueOrDefault("pulseflow.outcome"), outcome)
                && (errorType is null || Equals(m.Tags.GetValueOrDefault("error.type"), errorType))
                && m.Value >= 0);

        public void Dispose()
        {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }

    private static IMediator Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Probe());
        services.AddMediator(typeof(TelemetryTests).Assembly);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Successful_command_emits_a_span_and_a_duration()
    {
        using var capture = new Capture();

        await Build().Send(new TelemetryPing(5));

        var activity = capture.Single("command TelemetryPing");
        Assert.Equal("command", activity.GetTagItem("pulseflow.request.kind"));
        Assert.Equal(typeof(TelemetryPing).FullName, activity.GetTagItem("pulseflow.request.type"));
        Assert.Equal("success", activity.GetTagItem("pulseflow.outcome"));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.True(capture.HasMeasurement("TelemetryPing", "success"));
    }

    [Fact]
    public async Task Failed_result_is_tagged_but_not_marked_as_span_error()
    {
        using var capture = new Capture();

        await Build().Send(new TelemetryPing(0));

        var activity = capture.Single("command TelemetryPing");
        Assert.Equal("failure", activity.GetTagItem("pulseflow.outcome"));
        Assert.Equal("NotFound", activity.GetTagItem("error.type"));
        Assert.Equal("Ping.NotFound", activity.GetTagItem("pulseflow.error.code"));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.True(capture.HasMeasurement("TelemetryPing", "failure", "NotFound"));
    }

    [Fact]
    public async Task Exception_marks_the_span_as_error_and_is_rethrown()
    {
        using var capture = new Capture();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Build().Send(new TelemetryPing(-1)).AsTask());

        var activity = capture.Single("command TelemetryPing");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("exception", activity.GetTagItem("pulseflow.outcome"));
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
        Assert.True(capture.HasMeasurement("TelemetryPing", "exception", typeof(InvalidOperationException).FullName));
    }

    [Fact]
    public async Task Queries_notifications_and_streams_are_traced()
    {
        using var capture = new Capture();
        var mediator = Build();

        await mediator.Send(new GetThing(3));
        await mediator.Publish(new TelemetryNotice());
        var items = 0;
        await foreach (var _ in mediator.CreateStream(new CountTo(3)))
            items++;

        Assert.Contains(capture.Activities, a => a.DisplayName == "query GetThing" && Equals(a.GetTagItem("pulseflow.outcome"), "success"));
        capture.Single("notification TelemetryNotice", a => Equals(a.GetTagItem("pulseflow.outcome"), "success"));
        Assert.Contains(capture.Activities, a => a.DisplayName == "stream CountTo" && Equals(a.GetTagItem("pulseflow.stream.items"), 3));
        Assert.Equal(3, items);
        Assert.True(capture.HasMeasurement("TelemetryNotice", "success"));
    }

    [Fact]
    public async Task Handler_activities_are_children_of_the_request_span()
    {
        using var capture = new Capture();
        using var testSource = new ActivitySource("PulseFlow.Tests.Parent");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == testSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using (var parent = testSource.StartActivity("http request"))
        {
            await Build().Send(new TelemetryPing(1));

            var span = capture.Single("command TelemetryPing");
            Assert.Equal(parent!.TraceId, span.TraceId);
            Assert.Equal(parent.SpanId, span.ParentSpanId);
        }
    }
}
