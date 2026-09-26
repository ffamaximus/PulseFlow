namespace PulseFlow.Application.Mediator;

/// <summary>
/// Controls how notification handlers are executed by <see cref="IMediator.Publish{TNotification}"/>.
/// </summary>
public enum PublishStrategy
{
    /// <summary>
    /// Default. Runs handlers one after another in registration order. Every handler runs even if a
    /// previous one fails; afterwards a single failure is rethrown as-is and several failures are
    /// thrown as an <see cref="AggregateException"/>.
    /// </summary>
    Sequential,

    /// <summary>
    /// Starts all handlers at once and waits for all of them. Failures are reported like
    /// <see cref="Sequential"/>. Handlers share the same DI scope, so do not use this strategy when
    /// handlers depend on non thread-safe scoped services (for example an EF Core <c>DbContext</c>).
    /// </summary>
    Parallel,

    /// <summary>
    /// Runs handlers one after another and stops at the first exception, which is rethrown as-is.
    /// </summary>
    StopOnException
}
