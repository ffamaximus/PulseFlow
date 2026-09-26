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

    // Closed wrapper types, shared by every container: MakeGenericType is the expensive part, done once per process.
    // The wrapper instances themselves live per container in MediatorOptions (they cache per-container pipeline facts).
    private static readonly ConcurrentDictionary<Type, Type> CommandWrapperTypes = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> CommandWithResponseWrapperTypes = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> QueryWrapperTypes = new();
    private static readonly ConcurrentDictionary<(Type Request, Type Response), Type> StreamWrapperTypes = new();

    // Notification wrappers are stateless, so one instance per notification type is shared by every container
    // (a typed static cache measured faster for Publish than a per-container plan).
    private static readonly ConcurrentDictionary<Type, PublishWrapperBase> PublishWrappers = new();

    public Mediator(IServiceProvider provider, MediatorOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? new MediatorOptions();
    }

    public ValueTask<Result> Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = (CommandWrapperBase)_options.CommandPlans.GetOrAdd(command.GetType(), static t =>
            Activator.CreateInstance(CommandWrapperTypes.GetOrAdd(t, static x => typeof(CommandWrapper<>).MakeGenericType(x)))!);

        return MediatorTelemetry.IsEnabled
            ? SendInstrumented(wrapper, command, cancellationToken)
            : wrapper.Handle(command, _provider, cancellationToken);
    }

    public ValueTask<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = GetWrapper<ResponseWrapperBase<TResponse>, TResponse>(
            _options.CommandWithResponsePlans, CommandWithResponseWrapperTypes, typeof(CommandWrapper<,>), command.GetType());

        return MediatorTelemetry.IsEnabled
            ? SendInstrumented(wrapper, command, MediatorTelemetry.Command, cancellationToken)
            : wrapper.Handle(command, _provider, cancellationToken);
    }

    public ValueTask<Result<TResponse>> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = GetWrapper<ResponseWrapperBase<TResponse>, TResponse>(
            _options.QueryPlans, QueryWrapperTypes, typeof(QueryWrapper<,>), query.GetType());

        return MediatorTelemetry.IsEnabled
            ? SendInstrumented(wrapper, query, MediatorTelemetry.Query, cancellationToken)
            : wrapper.Handle(query, _provider, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = GetWrapper<StreamWrapperBase<TResponse>, TResponse>(
            _options.StreamPlans, StreamWrapperTypes, typeof(StreamQueryWrapper<,>), query.GetType());

        return MediatorTelemetry.IsEnabled
            ? CreateStreamInstrumented(wrapper, query, cancellationToken)
            : wrapper.Handle(query, _provider, cancellationToken);
    }

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var wrapper = PublishWrappers.GetOrAdd(notification.GetType(), static t =>
            (PublishWrapperBase)Activator.CreateInstance(typeof(PublishWrapper<>).MakeGenericType(t))!);

        return MediatorTelemetry.IsEnabled
            ? PublishInstrumented(wrapper, notification, cancellationToken)
            : wrapper.Handle(notification, _provider, _options.PublishStrategy, cancellationToken);
    }

    /// <summary>
    /// Per-container wrapper for a request type with a response. The fast path is a single dictionary lookup; the slow
    /// path creates the wrapper the first time the request type is sent in this container.
    /// </summary>
    private static TWrapper GetWrapper<TWrapper, TResponse>(
        ConcurrentDictionary<Type, object> plans,
        ConcurrentDictionary<(Type Request, Type Response), Type> wrapperTypes,
        Type openWrapperType,
        Type requestType)
        where TWrapper : class
    {
        if (plans.TryGetValue(requestType, out var cached) && cached is TWrapper typed)
            return typed;

        var closedType = wrapperTypes.GetOrAdd((requestType, typeof(TResponse)),
            static (key, open) => open.MakeGenericType(key.Request, key.Response), openWrapperType);
        var created = (TWrapper)Activator.CreateInstance(closedType)!;

        // A request type answering several response types keeps the first one cached; the others still work (uncached).
        return plans.GetOrAdd(requestType, created) as TWrapper ?? created;
    }

    #region Telemetry

    // Separate methods so the closures below are only allocated when a listener is attached.

    private ValueTask<Result> SendInstrumented(CommandWrapperBase wrapper, ICommand command, CancellationToken ct)
        => MediatorTelemetry.Track(MediatorTelemetry.Command, command.GetType(),
            () => wrapper.Handle(command, _provider, ct));

    private ValueTask<Result<TResponse>> SendInstrumented<TResponse>(ResponseWrapperBase<TResponse> wrapper, object request, string kind, CancellationToken ct)
        => MediatorTelemetry.Track(kind, request.GetType(),
            () => wrapper.Handle(request, _provider, ct));

    private IAsyncEnumerable<TResponse> CreateStreamInstrumented<TResponse>(StreamWrapperBase<TResponse> wrapper, object query, CancellationToken ct)
        => MediatorTelemetry.TrackStream(query.GetType(),
            () => wrapper.Handle(query, _provider, ct), ct);

    private ValueTask PublishInstrumented(PublishWrapperBase wrapper, object notification, CancellationToken ct)
        => MediatorTelemetry.TrackNotification(notification.GetType(),
            () => wrapper.Handle(notification, _provider, _options.PublishStrategy, ct));

    #endregion

    #region Pipeline

    /// <summary>
    /// What a wrapper has learned about its request type in this container. Written at most once per flag; a race only
    /// means one extra resolution, never a wrong result, because a flag is only set when the list was found empty.
    /// </summary>
    private abstract class WrapperState
    {
        /// <summary>No behaviors, processors nor exception handlers: the handler is called directly.</summary>
        public bool NoPipeline;
        public bool GeneralBehaviorsEmpty;
        public bool SpecificBehaviorsEmpty;
        public bool ProcessorsEmpty;
    }

    private static class Pipeline
    {
        public static IPipelineBehavior<TCommand, TResponse>[] ForCommand<TCommand, TResponse>(IServiceProvider provider, WrapperState state)
            where TCommand : IBaseCommand
            => Merge<TCommand, TResponse>(
                Resolve<IPipelineBehavior<TCommand, TResponse>>(provider, ref state.GeneralBehaviorsEmpty),
                Resolve<ICommandPipelineBehavior<TCommand, TResponse>>(provider, ref state.SpecificBehaviorsEmpty));

        public static IPipelineBehavior<TQuery, TResponse>[] ForQuery<TQuery, TResponse>(IServiceProvider provider, WrapperState state)
            where TQuery : IBaseQuery
            => Merge<TQuery, TResponse>(
                Resolve<IPipelineBehavior<TQuery, TResponse>>(provider, ref state.GeneralBehaviorsEmpty),
                Resolve<IQueryPipelineBehavior<TQuery, TResponse>>(provider, ref state.SpecificBehaviorsEmpty));

        /// <summary>Resolves all services of <typeparamref name="T"/>, skipping DI once the list is known to be empty.</summary>
        public static T[] Resolve<T>(IServiceProvider provider, ref bool knownEmpty)
        {
            if (knownEmpty)
                return [];

            var services = ToArray(provider.GetServices<T>());
            if (services.Length == 0)
                knownEmpty = true;
            return services;
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
            if (behaviors.Length == 0)
                return next();

            // Delegates are only built for the inner behaviors; the outermost one is called directly.
            for (var i = behaviors.Length - 1; i > 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.Handle(request, inner, ct);
            }

            return behaviors[0].Handle(request, next, ct);
        }
    }

    // Everything that runs around the handler, inside the behaviors: pre-processors, post-processors and exception handlers.
    private sealed class Processors<TRequest, TResponse>
    {
        private readonly IRequestPreProcessor<TRequest>[] _pre;
        private readonly IRequestPostProcessor<TRequest, TResponse>[] _post;
        private readonly IRequestExceptionHandler<TRequest, TResponse>[] _exceptionHandlers;

        private Processors(
            IRequestPreProcessor<TRequest>[] pre,
            IRequestPostProcessor<TRequest, TResponse>[] post,
            IRequestExceptionHandler<TRequest, TResponse>[] exceptionHandlers)
        {
            _pre = pre;
            _post = post;
            _exceptionHandlers = exceptionHandlers;
        }

        /// <summary>Null when the request has no processors nor exception handlers (remembered per container).</summary>
        public static Processors<TRequest, TResponse>? Resolve(IServiceProvider provider, WrapperState state)
        {
            if (state.ProcessorsEmpty)
                return null;

            var pre = Pipeline.ToArray(provider.GetServices<IRequestPreProcessor<TRequest>>());
            var post = Pipeline.ToArray(provider.GetServices<IRequestPostProcessor<TRequest, TResponse>>());
            var exceptionHandlers = Pipeline.ToArray(provider.GetServices<IRequestExceptionHandler<TRequest, TResponse>>());

            if (pre.Length == 0 && post.Length == 0 && exceptionHandlers.Length == 0)
            {
                state.ProcessorsEmpty = true;
                return null;
            }

            return new Processors<TRequest, TResponse>(pre, post, exceptionHandlers);
        }

        public RequestHandlerDelegate<TResponse> Wrap(TRequest request, RequestHandlerDelegate<TResponse> handler, CancellationToken ct)
        {
            if (_exceptionHandlers.Length == 0)
                return () => Run(request, handler, ct);

            return () => RunWithExceptionHandlers(request, handler, ct);
        }

        private async ValueTask<TResponse> Run(TRequest request, RequestHandlerDelegate<TResponse> handler, CancellationToken ct)
        {
            foreach (var processor in _pre)
                await processor.Process(request, ct).ConfigureAwait(false);

            var response = await handler().ConfigureAwait(false);

            foreach (var processor in _post)
                await processor.Process(request, response, ct).ConfigureAwait(false);

            return response;
        }

        private async ValueTask<TResponse> RunWithExceptionHandlers(TRequest request, RequestHandlerDelegate<TResponse> handler, CancellationToken ct)
        {
            try
            {
                return await Run(request, handler, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                foreach (var exceptionHandler in _exceptionHandlers)
                {
                    var outcome = await exceptionHandler.Handle(request, exception, ct).ConfigureAwait(false);
                    if (outcome.Handled)
                        return outcome.Response!;
                }

                throw;
            }
        }
    }

    #endregion

    #region Wrappers

    private abstract class CommandWrapperBase : WrapperState
    {
        public abstract ValueTask<Result> Handle(object command, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand> : CommandWrapperBase where TCommand : ICommand
    {
        public override ValueTask<Result> Handle(object command, IServiceProvider provider, CancellationToken ct)
        {
            var request = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand>>();

            // Fast path: once this request is known to have no pipeline in this container, go straight to the handler.
            if (NoPipeline)
                return handler.Handle(request, ct);

            var behaviors = Pipeline.ForCommand<TCommand, Result>(provider, this);
            var processors = Processors<TCommand, Result>.Resolve(provider, this);
            if (behaviors.Length == 0 && processors is null)
            {
                NoPipeline = true;
                return handler.Handle(request, ct);
            }

            return WithPipeline(request, handler, behaviors, processors, ct);
        }

        // Separate method: the closure (and its allocation) only exists when there is a pipeline.
        private static ValueTask<Result> WithPipeline(TCommand request, ICommandHandler<TCommand> handler,
            IPipelineBehavior<TCommand, Result>[] behaviors, Processors<TCommand, Result>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private abstract class ResponseWrapperBase<TResponse> : WrapperState
    {
        public abstract ValueTask<Result<TResponse>> Handle(object request, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class CommandWrapper<TCommand, TResponse> : ResponseWrapperBase<TResponse> where TCommand : ICommand<TResponse>
    {
        public override ValueTask<Result<TResponse>> Handle(object command, IServiceProvider provider, CancellationToken ct)
        {
            var request = (TCommand)command;
            var handler = provider.GetRequiredService<ICommandHandler<TCommand, TResponse>>();

            if (NoPipeline)
                return handler.Handle(request, ct);

            var behaviors = Pipeline.ForCommand<TCommand, Result<TResponse>>(provider, this);
            var processors = Processors<TCommand, Result<TResponse>>.Resolve(provider, this);
            if (behaviors.Length == 0 && processors is null)
            {
                NoPipeline = true;
                return handler.Handle(request, ct);
            }

            return WithPipeline(request, handler, behaviors, processors, ct);
        }

        private static ValueTask<Result<TResponse>> WithPipeline(TCommand request, ICommandHandler<TCommand, TResponse> handler,
            IPipelineBehavior<TCommand, Result<TResponse>>[] behaviors, Processors<TCommand, Result<TResponse>>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : ResponseWrapperBase<TResponse> where TQuery : IQuery<TResponse>
    {
        public override ValueTask<Result<TResponse>> Handle(object query, IServiceProvider provider, CancellationToken ct)
        {
            var request = (TQuery)query;
            var handler = provider.GetRequiredService<IQueryHandler<TQuery, TResponse>>();

            if (NoPipeline)
                return handler.Handle(request, ct);

            var behaviors = Pipeline.ForQuery<TQuery, Result<TResponse>>(provider, this);
            var processors = Processors<TQuery, Result<TResponse>>.Resolve(provider, this);
            if (behaviors.Length == 0 && processors is null)
            {
                NoPipeline = true;
                return handler.Handle(request, ct);
            }

            return WithPipeline(request, handler, behaviors, processors, ct);
        }

        private static ValueTask<Result<TResponse>> WithPipeline(TQuery request, IQueryHandler<TQuery, TResponse> handler,
            IPipelineBehavior<TQuery, Result<TResponse>>[] behaviors, Processors<TQuery, Result<TResponse>>? processors, CancellationToken ct)
            => Pipeline.Execute(request, behaviors, processors, () => handler.Handle(request, ct), ct);
    }

    private abstract class StreamWrapperBase<TResponse> : WrapperState
    {
        public abstract IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, CancellationToken ct);
    }

    private sealed class StreamQueryWrapper<TQuery, TResponse> : StreamWrapperBase<TResponse> where TQuery : IStreamQuery<TResponse>
    {
        public override IAsyncEnumerable<TResponse> Handle(object query, IServiceProvider provider, CancellationToken ct)
        {
            var request = (TQuery)query;
            var handler = provider.GetRequiredService<IStreamQueryHandler<TQuery, TResponse>>();

            if (NoPipeline)
                return handler.Handle(request, ct);

            var behaviors = Pipeline.Resolve<IStreamPipelineBehavior<TQuery, TResponse>>(provider, ref GeneralBehaviorsEmpty);
            if (behaviors.Length == 0)
            {
                NoPipeline = true;
                return handler.Handle(request, ct);
            }

            return WithPipeline(request, handler, behaviors, ct);
        }

        private static IAsyncEnumerable<TResponse> WithPipeline(TQuery request, IStreamQueryHandler<TQuery, TResponse> handler,
            IStreamPipelineBehavior<TQuery, TResponse>[] behaviors, CancellationToken ct)
        {
            StreamHandlerDelegate<TResponse> next = () => handler.Handle(request, ct);
            for (var i = behaviors.Length - 1; i > 0; i--)
            {
                var behavior = behaviors[i];
                var inner = next;
                next = () => behavior.Handle(request, inner, ct);
            }

            return behaviors[0].Handle(request, next, ct);
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
            var handlers = Pipeline.ToArray(provider.GetServices<INotificationHandler<TNotification>>());
            if (handlers.Length == 0)
                return ValueTask.CompletedTask;

            var typed = (TNotification)notification;

            // One handler: every strategy behaves the same, so skip the strategy machinery.
            if (handlers.Length == 1)
                return PublishSingle(handlers[0], typed, ct);

            return strategy switch
            {
                PublishStrategy.Parallel => new ValueTask(PublishParallel(handlers, typed, ct)),
                PublishStrategy.StopOnException => PublishStopOnException(handlers, typed, ct),
                _ => PublishSequential(handlers, typed, ct)
            };
        }

        // Kept out of Handle: a try/catch in Handle makes the JIT compile the whole method (and every Publish) less
        // efficiently, even when this path is not taken. Exceptions still surface through the returned ValueTask.
        private static ValueTask PublishSingle(INotificationHandler<TNotification> handler, TNotification notification, CancellationToken ct)
        {
            try
            {
                return handler.Handle(notification, ct);
            }
            catch (Exception ex)
            {
                return ValueTask.FromException(ex);
            }
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
