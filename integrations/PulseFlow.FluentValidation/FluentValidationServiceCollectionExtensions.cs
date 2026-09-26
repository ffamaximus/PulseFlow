using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application.Validation;

// Same namespace as AddMediator, so existing code keeps compiling after adding the package.
namespace PulseFlow.Application.Mediator;

public static class FluentValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FluentValidationBehavior{TRequest,TResponse}"/> so that the FluentValidation validators
    /// registered in DI (e.g. with <c>AddValidatorsFromAssemblyContaining&lt;T&gt;()</c>) run, asynchronously, before
    /// every command and query handler.
    /// </summary>
    public static IServiceCollection AddFluentValidationIntegration(this IServiceCollection services)
        => services.AddPipelineBehavior(typeof(FluentValidationBehavior<,>));
}
