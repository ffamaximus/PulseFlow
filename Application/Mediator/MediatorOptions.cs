using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// Options for <see cref="Mediator"/>. Configure them through
/// <c>services.AddMediator(options =&gt; ..., assemblies)</c>.
/// </summary>
public sealed class MediatorOptions
{
    /// <summary>
    /// How <see cref="IMediator.Publish{TNotification}"/> runs notification handlers.
    /// Defaults to <c>PublishStrategy.Sequential</c>, which is safe with scoped
    /// dependencies such as an EF Core <c>DbContext</c>.
    /// </summary>
    public PublishStrategy PublishStrategy { get; set; } = PublishStrategy.Sequential;

    /// <summary>
    /// Lifetime used by <c>AddMediator</c> for the handlers, processors and validators it discovers.
    /// Default <see cref="ServiceLifetime.Transient"/>. Must be set in the same <c>AddMediator</c> call that scans the assemblies.
    /// </summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Lifetime of <see cref="IMediator"/>. Default <see cref="ServiceLifetime.Scoped"/>, so handlers can depend on
    /// scoped services (e.g. a <c>DbContext</c>). Use <see cref="ServiceLifetime.Transient"/> to inject the mediator into
    /// singletons such as hosted services (then resolve it inside a scope). <see cref="ServiceLifetime.Singleton"/> is only
    /// safe when no handler, behavior or processor depends on scoped services.
    /// </summary>
    public ServiceLifetime MediatorLifetime { get; set; } = ServiceLifetime.Scoped;

    // Per container dispatch plans, one per request type: the wrapper that invokes the handler plus what it has learned
    // about the pipeline of that request in this container (e.g. "no behaviors"). One dictionary lookup per call.
    // (Notifications keep a static cache in Mediator: their wrappers have no per-container state.)
    internal ConcurrentDictionary<Type, object> CommandPlans { get; } = new();
    internal ConcurrentDictionary<Type, object> CommandWithResponsePlans { get; } = new();
    internal ConcurrentDictionary<Type, object> QueryPlans { get; } = new();
    internal ConcurrentDictionary<Type, object> StreamPlans { get; } = new();
}
