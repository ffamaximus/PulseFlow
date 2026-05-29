using System.Reflection;
using System.Text.Json;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Application.Validation;

/// <summary>
/// Pipeline behavior that will invoke FluentValidation validators if they are registered in the application's DI container.
/// This class uses reflection to avoid a compile-time dependency on FluentValidation.
/// </summary>
public class FluentValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IServiceProvider _provider;

    public FluentValidationBehavior(IServiceProvider provider)
    {
        _provider = provider;
    }

    public async Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken, Func<Task<TResponse>> next)
    {
        // Try to find FluentValidation.IValidator<> generic type definition in loaded assemblies
        var fluentValidatorGeneric = Type.GetType("FluentValidation.IValidator`1, FluentValidation");
        if (fluentValidatorGeneric == null)
        {
            // FluentValidation not present in app domain; skip
            return await next();
        }

        var closedValidatorType = fluentValidatorGeneric.MakeGenericType(typeof(TRequest));

        // Resolve all registered Fluent validators for TRequest
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(closedValidatorType);
        var validatorsObj = _provider.GetService(enumerableType);
        if (validatorsObj is not IEnumerable<object> validators)
            return await next();

        var failures = new List<(string PropertyName, string ErrorMessage)>();

        foreach (var validator in validators)
        {
            // Call Validate(request)
            var validateMethod = closedValidatorType.GetMethod("Validate", [typeof(object)])
                                 ?? closedValidatorType.GetMethod("Validate", [typeof(TRequest)]);

            if (validateMethod == null)
                continue;

            var validationResult = validateMethod.Invoke(validator, [request]);
            if (validationResult == null)
                continue;

            // FluentValidation.Results.ValidationResult has .IsValid and .Errors
            var isValidProp = validationResult.GetType().GetProperty("IsValid");
            var errorsProp = validationResult.GetType().GetProperty("Errors");

            var isValid = isValidProp != null && (bool)isValidProp.GetValue(validationResult)!;
            if (isValid) continue;

            var errors = errorsProp?.GetValue(validationResult) as IEnumerable<object>;
            if (errors == null) continue;

            foreach (var err in errors)
            {
                var propName = err.GetType().GetProperty("PropertyName")?.GetValue(err)?.ToString() ?? string.Empty;
                var message = err.GetType().GetProperty("ErrorMessage")?.GetValue(err)?.ToString() ?? string.Empty;
                failures.Add((propName, message));
            }
        }

        if (failures.Count == 0)
            return await next();

        // Map failures to a JSON error payload { errors: { PropertyName: ["msg"] } } and return failing Result/Result<T>
        var errorsByProperty = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToList());

        var payload = JsonSerializer.Serialize(new { errors = errorsByProperty });

        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Fail(payload);

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var genericArg = typeof(TResponse).GenericTypeArguments[0];
            var resultGeneric = typeof(Result<>).MakeGenericType(genericArg);
            var failMethod = resultGeneric.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static);
            var failed = failMethod!.Invoke(null, new object[] { payload });
            return (TResponse)failed!;
        }

        // Fallback: throw a ValidationException containing mapped failures
        var mappedFailures = failures.Select(f => new ValidationFailure(f.PropertyName, f.ErrorMessage)).ToList();
        throw new ValidationException(mappedFailures);
    }
}

