namespace PulseFlow.Application.Mediator;

/// <summary>
/// Runs after the handler of <typeparamref name="TRequest"/> returns (before the pipeline behaviors see the response),
/// with the <see cref="Result"/> / <see cref="Result{T}"/> it produced — including failed results. It does not run when
/// the handler throws. Closed implementations are discovered by <c>AddMediator</c>; open generic ones are registered
/// with <c>services.AddRequestPostProcessor(typeof(MyPostProcessor&lt;,&gt;))</c>.
/// </summary>
public interface IRequestPostProcessor<in TRequest, in TResponse>
{
    ValueTask Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
