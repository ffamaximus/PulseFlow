using System.Runtime.CompilerServices;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseFlow.Benchmarks.MediatRSide;

public sealed record Ping(int Value) : IRequest<int>;

public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public Task<int> Handle(Ping request, CancellationToken cancellationToken) => Task.FromResult(request.Value);
}

public sealed record Pinged(int Value) : INotification;

public sealed class PingedHandlerA : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PingedHandlerB : INotificationHandler<Pinged>
{
    public Task Handle(Pinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record CountTo(int Count) : IStreamRequest<int>;

public sealed class CountToHandler : IStreamRequestHandler<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(CountTo request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
    }
}

public sealed class PassThroughA<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

public sealed class PassThroughB<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

public static class MediatRSetup
{
    public static ServiceProvider Build(bool withBehaviors)
    {
        var services = new ServiceCollection();

        // MediatR 13+ logs a license warning without a key (MEDIATR_LICENSE_KEY); silence it for clean output.
        services.AddLogging(logging => logging.AddFilter("LuckyPennySoftware.MediatR.License", LogLevel.None));

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<PingHandler>();
            if (withBehaviors)
            {
                cfg.AddOpenBehavior(typeof(PassThroughA<,>));
                cfg.AddOpenBehavior(typeof(PassThroughB<,>));
            }
        });

        return services.BuildServiceProvider();
    }
}
