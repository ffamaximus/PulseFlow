using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// Options for <see cref="Mediator"/>. Configure them through
/// <c>services.AddMediator(options =&gt; ..., assemblies)</c>.
/// </summary>
public sealed class MediatorOptions
{
    // Per container: which (request, response) pipelines are known to have no behaviors / processors,
    // so they are not resolved from DI again on every request.
    private readonly ConcurrentDictionary<Type, bool> _emptyPipelines = new();

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

    internal bool IsKnownEmpty(Type pipeline) => _emptyPipelines.TryGetValue(pipeline, out var empty) && empty;

    internal void Remember(Type pipeline, bool empty) => _emptyPipelines.TryAdd(pipeline, empty);
}
