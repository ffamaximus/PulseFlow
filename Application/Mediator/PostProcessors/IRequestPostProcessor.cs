namespace PulseFlow.Application.Mediator.PostProcessors;

public interface IRequestPostProcessor<TRequest, TResponse>
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}
