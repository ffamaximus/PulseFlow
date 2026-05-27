using System.Collections.Concurrent;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Application.Mediator;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;
    private static readonly ConcurrentDictionary<Type, object> SendWrappers = new();
    private static readonly ConcurrentDictionary<Type, object> PublishWrappers = new();

    public Mediator(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        var requestType = command.GetType();
        var wrapper = (SendWrapperBase)SendWrappers.GetOrAdd(requestType, t =>
        {
            var wrapperType = typeof(SendCommandWrapper<>).MakeGenericType(t);
            return Activator.CreateInstance(wrapperType)!;
        });

        return await wrapper.Handle(command, _provider, cancellationToken);
    }

    public async Task<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        var requestType = query.GetType();
        var wrapper = (QueryWrapperBase<TResponse>)SendWrappers.GetOrAdd(requestType, t =>
        {
            var wrapperType = typeof(SendQueryWrapper<,>).MakeGenericType(t, typeof(TResponse));
            return Activator.CreateInstance(wrapperType)!;
        });

        return await wrapper.Handle(query, _provider, cancellationToken);
    }

    public async Task<IAsyncEnumerable<TResponse>> Send<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        var requestType = query.GetType();
        var wrapper = (StreamWrapperBase<TResponse>)SendWrappers.GetOrAdd(requestType, t =>
        {
            var wrapperType = typeof(SendStreamQueryWrapper<,>).MakeGenericType(t, typeof(TResponse));
            return Activator.CreateInstance(wrapperType)!;
        });

        return wrapper.Handle(query, _provider, cancellationToken);
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        var notificationType = notification.GetType();
        var wrapper = (PublishWrapperBase)PublishWrappers.GetOrAdd(notificationType, t =>
        {
            var wrapperType = typeof(PublishWrapper<>).MakeGenericType(t);
            return Activator.CreateInstance(wrapperType)!;
        });

        await wrapper.Handle(notification, _provider, cancellationToken);
    }

    #region Wrappers

    private abstract class SendWrapperBase
    {
        public abstract Task<Result> Handle(object command, IServiceProvider provider, CancellationToken ct);
    }

    private class SendCommandWrapper<TCommand> : SendWrapperBase where TCommand : ICommand
    {
        public override Task<Result> Handle(object command, IServiceProvider provider, CancellationToken ct)
        {
            var handler = provider.GetRequiredService<ICommandHandler<TCommand>>();
            var behaviors = provider.GetServices<IPipelineBehavior<TCommand, Result>>();

            Func<Task<Result>> seed = () => handler.Handle((TCommand)command, ct);

            return behaviors.Reverse().Aggregate(seed, (next, behavior) => () => behavior.Handle((TCommand)command, ct, next))();
        }
    }

    private abstract class QueryWrapperBase<TResponse>
    {
        public abstract Task<Result<TResponse>> Handle(object query, IServiceProvider provider, CancellationToken ct);
    }

    private class SendQueryWrapper<TQuery, TResponse> : QueryWrapperBase<TResponse> where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> Handle(object query, IServiceProvider provider, CancellationToken ct)
        {
            var handler = provider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
            var behaviors = provider.GetServices<IPipelineBehavior<TQuery, Result<TResponse>>>();

            Func<Task<Result<TResponse>>> seed = () => handler.Handle((TQuery)query, ct);

            return behaviors.Reverse().Aggregate(seed, (next, behavior) => () => behavior.Handle((TQuery)query, ct, next))();
        }
    }

    private abstract class StreamWrapperBase<TResponse>
    {
        public abstract IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, CancellationToken ct);
    }

    private class SendStreamQueryWrapper<TQuery, TResponse> : StreamWrapperBase<TResponse> where TQuery : IStreamQuery<TResponse>
    {
        public override IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, CancellationToken ct)
        {
            var handler = provider.GetRequiredService<IStreamQueryHandler<TQuery, TResponse>>();
            return handler.Handle((TQuery)query, ct);
        }
    }

    private abstract class PublishWrapperBase
    {
        public abstract Task Handle(object notification, IServiceProvider provider, CancellationToken ct);
    }

    private class PublishWrapper<TNotification> : PublishWrapperBase where TNotification : INotification
    {
        public override async Task Handle(object notification, IServiceProvider provider, CancellationToken ct)
        {
            var handlers = provider.GetServices<INotificationHandler<TNotification>>();
            var tasks = handlers.Select(handler => handler.Handle((TNotification)notification, ct));
            await Task.WhenAll(tasks);
        }
    }

    #endregion
}
