using Microsoft.Extensions.Logging;

namespace PulseFlow.Application.Mediator.Behaviors;

public class ExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ILogger<ExceptionBehavior<TRequest, TResponse>> _logger;

    public ExceptionBehavior(ILogger<ExceptionBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller cancelled: not an error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {RequestType}: {Message}", typeof(TRequest).Name, ex.Message);
            throw;
        }
    }
}
