using PulseFlow.Application.Commands;

namespace PulseFlow.Application.Mediator;

/// <summary>
/// A behavior that runs only for commands (<see cref="ICommand"/> and <see cref="ICommand{TResponse}"/>), never for
/// queries. Typical uses: transactions / unit of work, idempotency, auditing, outbox.
/// Register with <c>services.AddCommandBehavior(typeof(MyBehavior&lt;,&gt;))</c>.
/// </summary>
public interface ICommandPipelineBehavior<in TCommand, TResponse> : IPipelineBehavior<TCommand, TResponse>
    where TCommand : IBaseCommand
{
}
