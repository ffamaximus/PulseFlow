using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;
using PulseFlow.Application.Validation;
using PulseFlow.Domain;

namespace PulseFlow.Application.Mediator;

public static class ServiceCollectionExtensions
{
    // Open generic contracts discovered by assembly scanning.
    private static readonly Type[] ScannedContracts =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IStreamQueryHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IDomainEventHandler<>),
        typeof(IRequestValidator<>),
        typeof(IRequestPreProcessor<>),
        typeof(IRequestPostProcessor<,>),
        typeof(IRequestExceptionHandler<,>)
    ];

    /// <summary>
    /// Registers the Mediator and scans the provided assemblies for implementations of
    /// <see cref="ICommandHandler{TCommand}"/>, <see cref="ICommandHandler{TCommand,TResponse}"/>, <see cref="IQueryHandler{TQuery,TResponse}"/>,
    /// <see cref="IStreamQueryHandler{TQuery,TResponse}"/>, <see cref="INotificationHandler{TNotification}"/>,
    /// <see cref="IDomainEventHandler{TEvent}"/>, <see cref="IRequestValidator{T}"/>, <see cref="IRequestPreProcessor{TRequest}"/>
    /// <see cref="IRequestPostProcessor{TRequest,TResponse}"/> and <see cref="IRequestExceptionHandler{TRequest,TResponse}"/>.
    /// Usage in Program.cs: <c>services.AddMediator(typeof(AnyTypeInAssembly).Assembly)</c>.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[]? assemblies)
        => services.AddMediator(configure: null, assemblies);

    /// <summary>
    /// Convenience overload that scans all currently loaded assemblies.
    /// Prefer passing the assemblies explicitly: assemblies that are not loaded yet are not scanned.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services)
        => services.AddMediator(configure: null);

    /// <summary>
    /// Registers the Mediator with custom <see cref="MediatorOptions"/> (for example the
    /// <see cref="MediatorOptions.PublishStrategy"/>) and scans the provided assemblies.
    /// Safe to call more than once: nothing is registered twice.
    /// </summary>
    /// <remarks>
    /// Open generic handler classes (for example <c>class Handler&lt;T&gt; : ICommandHandler&lt;MyCommand&lt;T&gt;&gt;</c>)
    /// are skipped because the container cannot build them from a closed service type; register them manually.
    /// </remarks>
    public static IServiceCollection AddMediator(
        this IServiceCollection services,
        Action<MediatorOptions>? configure,
        params Assembly[]? assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = GetOrAddOptions(services);
        configure?.Invoke(options);

        // Scoped by default so handlers can use scoped dependencies; see MediatorOptions.MediatorLifetime.
        services.TryAdd(new ServiceDescriptor(typeof(IMediator), typeof(Mediator), options.MediatorLifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.MediatorLifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.MediatorLifetime));

        services.TryAddScoped<DomainEventDispatcher>();
        services.TryAddScoped<IDomainEventDispatcher>(sp => sp.GetRequiredService<DomainEventDispatcher>());

        var assembliesToScan = assemblies is { Length: > 0 }
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assembliesToScan.Distinct())
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                // Abstract classes, interfaces and open generic classes cannot be instantiated for a closed service type.
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                    continue;

                foreach (var contract in type.GetInterfaces())
                {
                    if (!contract.IsGenericType || Array.IndexOf(ScannedContracts, contract.GetGenericTypeDefinition()) < 0)
                        continue;

                    // TryAddEnumerable skips an identical (service, implementation) pair, so calling
                    // AddMediator twice, or scanning the same assembly twice, never duplicates handlers.
                    // Lifetime: MediatorOptions.HandlerLifetime (Transient by default).
                    services.TryAddEnumerable(new ServiceDescriptor(contract, type, options.HandlerLifetime));
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Registers an open generic behavior (e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>) that runs for every command and query.
    /// Behaviors run in registration order; general behaviors wrap the command/query-specific ones.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(IPipelineBehavior<,>), openBehaviorType);

    /// <summary>
    /// Same as <see cref="AddPipelineBehavior(IServiceCollection, Type)"/> with an explicit lifetime. Stateless behaviors
    /// (logging, validation, metrics) can be <see cref="ServiceLifetime.Singleton"/>, which avoids creating them on every request.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openBehaviorType, ServiceLifetime lifetime)
        => services.AddBehavior(typeof(IPipelineBehavior<,>), openBehaviorType, lifetime);

    /// <summary>
    /// Registers an open generic <see cref="ICommandPipelineBehavior{TCommand,TResponse}"/> that runs only for commands
    /// (e.g. a transaction behavior).
    /// </summary>
    public static IServiceCollection AddCommandBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(ICommandPipelineBehavior<,>), openBehaviorType);

    /// <summary>Same as <see cref="AddCommandBehavior(IServiceCollection, Type)"/> with an explicit lifetime.</summary>
    public static IServiceCollection AddCommandBehavior(this IServiceCollection services, Type openBehaviorType, ServiceLifetime lifetime)
        => services.AddBehavior(typeof(ICommandPipelineBehavior<,>), openBehaviorType, lifetime);

    /// <summary>
    /// Registers an open generic <see cref="IQueryPipelineBehavior{TQuery,TResponse}"/> that runs only for queries
    /// (e.g. a caching behavior).
    /// </summary>
    public static IServiceCollection AddQueryBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(IQueryPipelineBehavior<,>), openBehaviorType);

    /// <summary>Same as <see cref="AddQueryBehavior(IServiceCollection, Type)"/> with an explicit lifetime.</summary>
    public static IServiceCollection AddQueryBehavior(this IServiceCollection services, Type openBehaviorType, ServiceLifetime lifetime)
        => services.AddBehavior(typeof(IQueryPipelineBehavior<,>), openBehaviorType, lifetime);

    /// <summary>
    /// Registers an open generic <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/> (e.g. <c>typeof(MyStreamBehavior&lt;,&gt;)</c>)
    /// that wraps every stream query.
    /// </summary>
    public static IServiceCollection AddStreamBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(IStreamPipelineBehavior<,>), openBehaviorType);

    /// <summary>
    /// Registers an open generic <see cref="IRequestPreProcessor{TRequest}"/> (e.g. <c>typeof(AuditPreProcessor&lt;&gt;)</c>)
    /// that runs before every command and query handler. Closed pre-processors are discovered by <c>AddMediator</c>.
    /// </summary>
    public static IServiceCollection AddRequestPreProcessor(this IServiceCollection services, Type openProcessorType)
        => services.AddBehavior(typeof(IRequestPreProcessor<>), openProcessorType);

    /// <summary>
    /// Registers an open generic <see cref="IRequestPostProcessor{TRequest,TResponse}"/> (e.g. <c>typeof(AuditPostProcessor&lt;,&gt;)</c>)
    /// that runs after every command and query handler. Closed post-processors are discovered by <c>AddMediator</c>.
    /// </summary>
    public static IServiceCollection AddRequestPostProcessor(this IServiceCollection services, Type openProcessorType)
        => services.AddBehavior(typeof(IRequestPostProcessor<,>), openProcessorType);

    /// <summary>
    /// Registers an open generic <see cref="IRequestExceptionHandler{TRequest,TResponse}"/> (e.g. <c>typeof(ConcurrencyToConflict&lt;,&gt;)</c>)
    /// for every command and query. Closed exception handlers are discovered by <c>AddMediator</c>.
    /// </summary>
    public static IServiceCollection AddRequestExceptionHandler(this IServiceCollection services, Type openHandlerType)
        => services.AddBehavior(typeof(IRequestExceptionHandler<,>), openHandlerType);

    private static IServiceCollection AddBehavior(this IServiceCollection services, Type contract, Type openBehaviorType,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        var arity = contract.GetGenericArguments().Length;
        if (!openBehaviorType.IsGenericTypeDefinition || openBehaviorType.GetGenericArguments().Length != arity)
            throw new ArgumentException(
                $"'{openBehaviorType}' must be an open generic type with {arity} type parameter(s), e.g. typeof(My{contract.Name.Split('`')[0].TrimStart('I')}<{new string(',', arity - 1)}>).",
                nameof(openBehaviorType));

        if (!openBehaviorType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == contract))
            throw new ArgumentException($"'{openBehaviorType.Name}' does not implement {contract.Name}.", nameof(openBehaviorType));

        services.TryAddEnumerable(new ServiceDescriptor(contract, openBehaviorType, lifetime));
        return services;
    }

    private static MediatorOptions GetOrAddOptions(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(MediatorOptions) && descriptor.ImplementationInstance is MediatorOptions existing)
                return existing;
        }

        var options = new MediatorOptions();
        services.AddSingleton(options);
        return options;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
