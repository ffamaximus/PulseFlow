namespace PulseFlow.Application.Validation;

/// <summary>
/// Synchronous validator for a command or query, run by <see cref="ValidationBehavior{TRequest,TResponse}"/>.
/// Discovered automatically by <c>AddMediator(assemblies)</c>.
/// </summary>
/// <remarks>
/// Named <c>IRequestValidator</c> (not <c>IValidator</c>) so it never clashes with FluentValidation's types when both
/// namespaces are imported in the same file.
/// </remarks>
public interface IRequestValidator<in T>
{
    RequestValidationResult Validate(T instance);
}
