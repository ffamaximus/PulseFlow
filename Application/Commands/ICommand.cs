namespace PulseFlow.Application.Commands;

/// <summary>
/// Marker shared by every command, with or without a response. Use it as a generic constraint
/// (for example in an <see cref="Mediator.ICommandPipelineBehavior{TCommand,TResponse}"/>).
/// </summary>
public interface IBaseCommand { }

/// <summary>A command that changes state and returns only success or failure (<see cref="Result"/>).</summary>
public interface ICommand : IBaseCommand { }

/// <summary>A command that changes state and returns a value on success (<see cref="Result{T}"/>), e.g. the id of a created entity.</summary>
public interface ICommand<TResponse> : IBaseCommand { }
