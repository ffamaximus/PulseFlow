using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PulseFlow.Application.Mediator.Behaviors;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            _logger.LogInformation("{RequestType} executed in {ElapsedMilliseconds}ms", typeof(TRequest).Name, (long)elapsed.TotalMilliseconds);
        }
    }
}
