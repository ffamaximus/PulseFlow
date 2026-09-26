namespace PulseFlow.Application.Commands;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    ValueTask<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    ValueTask<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}
