using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Benchmarks.MediatorSide;

public sealed record Ping(int Value) : ICommand<int>;

public sealed class PingHandler : ICommandHandler<Ping, int>
{
    public ValueTask<int> Handle(Ping command, CancellationToken cancellationToken)
        => ValueTask.FromResult(command.Value);
}

public sealed record Pinged(int Value) : INotification;

public sealed class PingedHandlerA : INotificationHandler<Pinged>
{
    public ValueTask Handle(Pinged notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class PingedHandlerB : INotificationHandler<Pinged>
{
    public ValueTask Handle(Pinged notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed record CountTo(int Count) : IStreamQuery<int>;

public sealed class CountToHandler : IStreamQueryHandler<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(CountTo query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < query.Count; i++)
            yield return i;
    }
}

public sealed class PassThroughA<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        => next(message, cancellationToken);
}

public sealed class PassThroughB<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        => next(message, cancellationToken);
}

public static class MediatorSetup
{
    // Single AddMediator call site: the source generator reads its options at compile time.
    // Behaviors are registered in DI per container (supported by Mediator), so the same generated code serves both setups.
    public static ServiceProvider Build(bool withBehaviors)
    {
        var services = new ServiceCollection();

        services.AddMediator((MediatorOptions options) =>
        {
            options.ServiceLifetime = ServiceLifetime.Singleton; // Mediator's default and recommended lifetime
        });

        if (withBehaviors)
        {
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(PassThroughA<,>));
            services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(PassThroughB<,>));
        }

        return services.BuildServiceProvider();
    }
}
