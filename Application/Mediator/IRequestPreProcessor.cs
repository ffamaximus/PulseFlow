namespace PulseFlow.Application.Mediator;

/// <summary>
/// Runs before the handler of <typeparamref name="TRequest"/> (after every pipeline behavior). Use it for simple
/// "before" logic such as auditing or enriching the request, without writing a full behavior.
/// Closed implementations are discovered by <c>AddMediator</c>; open generic ones are registered with
/// <c>services.AddRequestPreProcessor(typeof(MyPreProcessor&lt;&gt;))</c>.
/// </summary>
public interface IRequestPreProcessor<in TRequest>
{
    ValueTask Process(TRequest request, CancellationToken cancellationToken);
}
