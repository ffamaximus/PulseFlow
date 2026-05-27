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

    public async Task<TResponse> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResponse>> next)
    {
        var sw = Stopwatch.StartNew();

        var response = await next();

        sw.Stop();
        _logger.LogInformation("{RequestType} executed in {ElapsedMilliseconds}ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
