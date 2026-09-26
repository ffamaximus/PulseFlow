using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;

namespace PulseFlow.Application.Mediator;

public class Mediator : IMediator
{
    private readonly IServiceProvider _provider;
    private readonly MediatorOptions _options;

    // One cache per request kind, keyed by (request type, response type) where there is a response,
    // so a wrapper of the wrong kind can never come back from the cache.
    private static readonly ConcurrentDictionary<Type, CommandWrapperBase> CommandWrappers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> CommandWithResponseWrappers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> QueryWrappers = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), object> StreamWrappers = new();
    private static readonly ConcurrentDictionary<Type, PublishWrapperBase> PublishWrappers = new();

    public Mediator(IServiceProvider provider, MediatorOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? new MediatorOptions();
    }

    public ValueTask<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = CommandWrappers.GetOrAdd(command.GetType(), static t =>
            (CommandWrapperBase)Activator.CreateInstance(typeof(CommandWrapper<>).MakeGenericType(t))!);

        return wrapper.Handle(command, _provider, _options, cancellationToken);
    }

    public ValueTask<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (ResponseWrapperBase<TResponse>)CommandWithResponseWrappers.GetOrAdd((command.GetType(), typeof(TResponse)), static key =>
            Activator.CreateInstance(typeof(CommandWrapper<,>).MakeGenericType(key.Request, key.Response))!);

        return wrapper.Handle(command, _provider, _options, cancellationToken);
    }

    public ValueTask<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (ResponseWrapperBase<TResponse>)QueryWrappers.GetOrAdd((query.GetType(), typeof(TResponse)), static key =>
            Activator.CreateInstance(typeof(QueryWrapper<,>).MakeGenericType(key.Request, key.Response))!);

        return wrapper.Handle(query, _provider, _options, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = (StreamWrapperBase<TResponse>)StreamWrappers.GetOrAdd((query.GetType(), typeof(TResponse)), static key =>
            Activator.CreateInstance(typeof(StreamQueryWrapper<,>).MakeGenericType(key.Request, key.Response))!);

        return wrapper.Handle(query, _provider, _options, cancellationToken);
    }

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var wrapper = PublishWrappers.GetOrAdd(notification.GetType(), static t =>
            (PublishWrapperBase)Activator.CreateInstance(typeof(PublishWrapper<>).MakeGenericType(t))!);

        return wrapper.Handle(notification, _provider, _options.PublishStrategy, cancellationToken);
    }

    #region Pipeline

    private static class Pipeline
    {
        public static IPipelineBehavior<TCommand, TResponse>[] ForCommand<TCommand, TResponse>(IServiceProvider provider, MediatorOptions options)
            where TCommand : IBaseCommand
        {
            var key = typeof(ICommandPipelineBehavior<TCommand, TResponse>);
            if (options.IsKnownEmpty(key))
                return [];

            var behaviors = Merge<TCommand, TResponse>(
                provider.GetServices<IPipelineBehavior<TCommand, TResponse>>(),
                provider.GetServices<ICommandPipelineBehavior<TCommand, TResponse>>());

            options.Remember(key, behaviors.Length == 0);
            return behaviors;
        }

        public static IPipelineBehavior<TQuery, TResponse>[] ForQuery<TQuery, TResponse>(IServiceProvider provider, MediatorOptions options)
            where TQuery : IBaseQuery
        {
            var key = typeof(IQueryPipelineBehavior<TQuery, TResponse>);
            if (options.IsKnownEmpty(key))
                return [];

            var behaviors = Merge<TQuery, TResponse>(
                provider.GetServices<IPipelineBehavior<TQuery, TResponse>>(),
                provider.GetServices<IQueryPipelineBehavior<TQuery, TResponse>>());

            options.Remember(key, behaviors.Length == 0);
            return behaviors;
        }

        public static T[] ToArray<T>(IEnumerable<T> services) => services as T[] ?? services.ToArray();

        // General behaviors first (outermost), then the kind-specific ones.
        private static IPipelineBehavior<TRequest, TResponse>[] Merge<TRequest, TResponse>(
            IEnumerable<IPipelineBehavior<TRequest, TResponse>> general,
            IEnumerable<IPipelineBehavior<TRequest, TResponse>> specific)
        {
            var first = ToArray(general);
            var second = ToArray(specific);

            if (second.Length == 0) return first;
            if (first.Length == 0) return second;

            var all = new IPipelineBehavior<TRequest, TResponse>[first.Length + second.Length];
            Array.Copy(first, all, first.Length);
            Array.Copy(second, 0, all, first.Length, second.Length);
            return all;
        }

        // behaviors (outermost, in order) -> pre-processors -> handler -> post-processors
        public static ValueTask<TResponse> Execute<TRequest, TResponse>(
            TRequest request,
            IPipelineBehavior<TRequest, TResponse>[] behaviors,
            Processors<TRequest, TResponse>? processors,
            RequestHandlerDelegate<TResponse> handler,
            CancellationToken ct)
        {
            var next = processors is null ? handler : processors.Wrap(request, handler, ct);
            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.Handle(request, inner, ct);
            }

            return next();
        }
    }

    private sealed class Processors<TRequest, TResponse>
    {
        private readonly IRequestPreProcessor<TRequest>[] _pre;
        private readonly IRequestPostProcessor<TRequest, TResponse>[] _post;

        private Processors(IRequestPreProcessor<TRequest>[] pre, IRequestPostProcessor<TRequest, TResponse>[] post)
        {
            _pre = pre;
            _post = post;
        }

        /// <summary>Null when the request has no pre- or post-processors (remembered per container).</summary>
        public static Processors<TRequest, TResponse>? Resolve(IServiceProvider provider, MediatorOptions options)
        {
            var key = typeof(IRequestPostProcessor<TRequest, TResponse>);
            if (options.IsKnownEmpty(key))
                return null;

            var pre = Pipeline.ToArray(provider.GetServices<IRequestPreProcessor<TRequest>>());
            var post = Pipeline.ToArray(provider.GetServices<IRequestPostProcessor<TRequest, TResponse>>());
            var empty = pre.Length == 0 && post.Length == 0;

            options.Remember(key, empty);
            return empty ? null : new Processors<TRequest, TResponse>(pre, post);
        }

        public RequestHandlerDelegate<TResponse> Wrap(TRequest request, RequestHandlerDelegate<TResponse> handler, CancellationToken ct)
            => async () =>
            {
                foreach (var processor in _pre)
                    await processor.Process(request, ct).ConfigureAwait(false);

                var response = await handler().ConfigureAwait(false);

                foreach (var processor in _post)
                    await processor.Process(request, response, ct).ConfigureAwait(false);

                return response;
            };
    }

    #endregion

    #region Wrappers

    private abstract class CommandWrapperBase
    {
        public abstract ValueTask<Result> Handle(object command, IServiceProvider provider, MediatorOptions options, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand> : CommandWrapperBase where TCommand : ICommand
    {
        public override ValueTask<Result> Handle(object command, IServiceProvider provider, MediatorOptions options, CancellationToken ct)
        {
            var request = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand>>();
            var behaviors = Pipeline.ForCommand<TCommand, Result>(provider, options);
            var processors = Processors<TCommand, Result>.Resolve(provider, options);

            // Fast path: no behaviors, no processors, no delegate allocation.
            return behaviors.Length == 0 && processors is null
                ? handler.Handle(request, ct)
                : WithPipeline(request, handler, behaviors, processors, ct);
        }

        // Separate method: the closure (and its allocation) only exists when there is a pipeline.
        private static ValueTask<Result> WithPipeline(TCommand request, ICommandHandler<TCommand> handler,
            IPipelineBehavior<TCommand, Result>[] behaviors, Processors<TCommand, Result>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private abstract class ResponseWrapperBase<TResponse>
    {
        public abstract ValueTask<Result<TResponse>> Handle(object request, IServiceProvider provider, MediatorOptions options, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand, TResponse> : ResponseWrapperBase<TResponse> where TCommand : ICommand<TResponse>
    {
        public override ValueTask<Result<TResponse>> Handle(object command, IServiceProvider provider, MediatorOptions options, CancellationToken ct)
        {
            var request = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();
            var behaviors = Pipeline.ForCommand<TCommand, Result<TResponse>>(provider, options);
            var processors = Processors<TCommand, Result<TResponse>>.Resolve(provider, options);

            return behaviors.Length == 0 && processors is null
                ? handler.Handle(request, ct)
                : WithPipeline(request, handler, behaviors, processors, ct);
        }

        private static ValueTask<Result<TResponse>> WithPipeline(TCommand request, ICommandHandler<TCommand, TResponse> handler,
            IPipelineBehavior<TCommand, Result<TResponse>>[] behaviors, Processors<TCommand, Result<TResponse>>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : ResponseWrapperBase<TResponse> where TQuery : IQuery<TResponse>
    {
        public override ValueTask<Result<TResponse>> Handle(object query, IServiceProvider provider, MediatorOptions options, CancellationToken ct)
        {
            var request = (TQuery)query;
            var handler = provider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();
            var behaviors = Pipeline.ForQuery<TQuery, Result<TResponse>>(provider, options);
            var processors = Processors<TQuery, Result<TResponse>>.Resolve(provider, options);

            return behaviors.Length == 0 && processors is null
                ? handler.Handle(request, ct)
                : WithPipeline(request, handler, behaviors, processors, ct);
        }

        private static ValueTask<Result<TResponse>> WithPipeline(TQuery request, IQueryHandler<TQuery, TResponse> handler,
            IPipelineBehavior<TQuery, Result<TResponse>>[] behaviors, Processors<TQuery, Result<TResponse>>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private abstract class StreamWrapperBase<TResponse>
    {
        public abstract IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, MediatorOptions options, CancellationToken ct);
    }

    private sealed class StreamQueryWrapper<TQuery, TResponse> : StreamWrapperBase<TResponse> where TQuery : IStreamQuery<TResponse>
    {
        public override IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, MediatorOptions options, CancellationToken ct)
        {
            var request = (TQuery)query;
            var handler = provider.GetRequiredService<IStreamQueryHandler<TQuery, TResponse>>();
            var behaviors = ResolveBehaviors(provider, options);

            return behaviors.Length == 0
                ? handler.Handle(request, ct)
                : WithPipeline(request, handler, behaviors, ct);
        }

        private static IStreamPipelineBehavior<TQuery, TResponse>[] ResolveBehaviors(IServiceProvider provider, MediatorOptions options)
        {
            var key = typeof(IStreamPipelineBehavior<TQuery, TResponse>);
            if (options.IsKnownEmpty(key))
                return [];

            var behaviors = Pipeline.ToArray(provider.GetServices<IStreamPipelineBehavior<TQuery, TResponse>>());
            options.Remember(key, behaviors.Length == 0);
            return behaviors;
        }

        private static IAsyncEnumerable<TResponse> WithPipeline(TQuery request, IStreamQueryHandler<TQuery, TResponse> handler,
            IStreamPipelineBehavior<TQuery, TResponse>[] behaviors, CancellationToken ct)
        {
            StreamHandlerDelegate<TResponse> next = () => handler.Handle(request, ct);
            for (var i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.Handle(request, inner, ct);
            }

            return next();
        }
    }

    private abstract class PublishWrapperBase
    {
        public abstract ValueTask Handle(object notification, IServiceProvider provider, PublishStrategy strategy, CancellationToken ct);
    }

    private sealed class PublishWrapper<TNotification> : PublishWrapperBase where TNotification : INotification
    {
        public override ValueTask Handle(object notification, IServiceProvider provider, PublishStrategy strategy, CancellationToken ct)
        {
            var handlers = provider.GetServices<INotificationHandler<TNotification>>().ToArray();
            if (handlers.Length == 0)
                return ValueTask.CompletedTask;

            var typed = (TNotification)notification;

            return strategy switch
            {
                PublishStrategy.Parallel => new ValueTask(PublishParallel(handlers, typed, ct)),
                PublishStrategy.StopOnException => PublishStopOnException(handlers, typed, ct),
                _ => PublishSequential(handlers, typed, ct)
            };
        }

        private static async ValueTask PublishSequential(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        {
            List<Exception>? errors = null;

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.Handle(notification, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    (errors ??= new List<Exception>()).Add(ex);
                }
            }

            ThrowIfAny(errors);
        }

        private static async ValueTask PublishStopOnException(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        {
            foreach (var handler in handlers)
                await handler.Handle(notification, ct).ConfigureAwait(false);
        }

        private static async Task PublishParallel(INotificationHandler<TNotification>[] handlers, TNotification notification, CancellationToken ct)
        {
            var tasks = new Task[handlers.Length];
            for (var i = 0; i < handlers.Length; i++)
                tasks[i] = InvokeSafely(handlers[i], notification, ct);

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                var errors = tasks
                    .Where(t => t.IsFaulted)
                    .SelectMany(t => t.Exception!.InnerExceptions)
                    .ToList();

                if (errors.Count == 0)
                    throw; // only cancellations

                ThrowIfAny(errors);
            }
        }

        // A handler that throws synchronously (non-async method) must not prevent the others from starting.
        private static Task InvokeSafely(INotificationHandler<TNotification> handler, TNotification notification, CancellationToken ct)
        {
            try
            {
                return handler.Handle(notification, ct).AsTask();
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }
    }

    // One failure: rethrow it preserving type and stack trace. Several: AggregateException with all of them.
    private static void ThrowIfAny(List<Exception>? errors)
    {
        if (errors is null || errors.Count == 0)
            return;

        if (errors.Count == 1)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();

        throw new AggregateException(errors);
    }

    #endregion
}
