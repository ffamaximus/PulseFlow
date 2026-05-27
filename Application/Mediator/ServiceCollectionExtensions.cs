using System.Reflection;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.Application.Mediator;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the kernel Mediator and scans the provided assemblies for implementations of
    /// <see cref="ICommandHandler{TCommand}"/> and <see cref="IQueryHandler{TQuery,TResult}"/> to register them in the DI container.
    /// Usage in Program.cs: <c>services.AddMediator(typeof(AnyTypeInAssembly).Assembly)</c>.
    /// </summary>
    private static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[]? assemblies)
    {
        // Registered as Scoped to allow the use of Scoped dependencies within Handlers
        services.AddScoped<IMediator, Mediator>();

        var assembliesToScan = assemblies is { Length: > 0 }
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        var handlerPairs = new List<(Type service, Type implementation)>();

        foreach (var assembly in assembliesToScan)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            handlerPairs.AddRange(from type in types.Where(t => t is { IsAbstract: false, IsInterface: false }) let interfaces = type.GetInterfaces().Where(i => i.IsGenericType).ToArray() from iface in interfaces let generic = iface.GetGenericTypeDefinition() where generic == typeof(ICommandHandler<>) || generic == typeof(IQueryHandler<,>) select (iface, type));
        }

        foreach (var (service, impl) in handlerPairs.Distinct())
        {
            // Register all handlers as Transient; their actual lifetime will be managed within the Mediator's scope
            services.AddTransient(service, impl);
        }

        return services;
    }

    /// <summary>
    /// Convenience overload that scans all currently loaded assemblies.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services)
        => services.AddMediator([]);
}
