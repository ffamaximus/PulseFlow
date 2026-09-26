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
        typeof(IRequestValidator<>)
    ];

    /// <summary>
    /// Registers the Mediator and scans the provided assemblies for implementations of
    /// <see cref="ICommandHandler{TCommand}"/>, <see cref="ICommandHandler{TCommand,TResponse}"/>, <see cref="IQueryHandler{TQuery,TResponse}"/>,
    /// <see cref="IStreamQueryHandler{TQuery,TResponse}"/>, <see cref="INotificationHandler{TNotification}"/>,
    /// <see cref="IDomainEventHandler{TEvent}"/> and <see cref="IRequestValidator{T}"/>.
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

        // Registered as Scoped to allow the use of Scoped dependencies within Handlers
        services.TryAddScoped<IMediator, Mediator>();

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
                    // Handlers are Transient; their dependencies follow the scope that resolves the Mediator.
                    services.TryAddEnumerable(ServiceDescriptor.Transient(contract, type));
                }
            }
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="FluentValidationBehavior{TRequest,TResponse}"/> so that the FluentValidation validators
    /// registered in DI (e.g. with <c>AddValidatorsFromAssemblyContaining&lt;T&gt;()</c>) run, asynchronously, before
    /// every command and query handler.
    /// </summary>
    public static IServiceCollection AddFluentValidationIntegration(this IServiceCollection services)
        => services.AddPipelineBehavior(typeof(FluentValidationBehavior<,>));

    /// <summary>
    /// Registers an open generic behavior (e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>) that runs for every command and query.
    /// Behaviors run in registration order; general behaviors wrap the command/query-specific ones.
    /// </summary>
    public static IServiceCollection AddPipelineBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(IPipelineBehavior<,>), openBehaviorType);

    /// <summary>
    /// Registers an open generic <see cref="ICommandPipelineBehavior{TCommand,TResponse}"/> that runs only for commands
    /// (e.g. a transaction behavior).
    /// </summary>
    public static IServiceCollection AddCommandBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(ICommandPipelineBehavior<,>), openBehaviorType);

    /// <summary>
    /// Registers an open generic <see cref="IQueryPipelineBehavior{TQuery,TResponse}"/> that runs only for queries
    /// (e.g. a caching behavior).
    /// </summary>
    public static IServiceCollection AddQueryBehavior(this IServiceCollection services, Type openBehaviorType)
        => services.AddBehavior(typeof(IQueryPipelineBehavior<,>), openBehaviorType);

    private static IServiceCollection AddBehavior(this IServiceCollection services, Type contract, Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        if (!openBehaviorType.IsGenericTypeDefinition || openBehaviorType.GetGenericArguments().Length != 2)
            throw new ArgumentException($"'{openBehaviorType}' must be an open generic type with two type parameters, e.g. typeof(MyBehavior<,>).", nameof(openBehaviorType));

        if (!openBehaviorType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == contract))
            throw new ArgumentException($"'{openBehaviorType.Name}' does not implement {contract.Name}.", nameof(openBehaviorType));

        services.TryAddEnumerable(ServiceDescriptor.Transient(contract, openBehaviorType));
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
