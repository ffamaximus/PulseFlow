namespace PulseFlow.Application.Mediator;

/// <summary>
/// Runtime options for <see cref="Mediator"/>. Configure them through
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
}
