namespace PulseFlow.Application.Mediator.PreProcessors;

public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}
