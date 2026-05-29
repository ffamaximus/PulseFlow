using System.Reflection;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using PulseFlow.Application.Mediator;

namespace PulseFlow.Application.Validation;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        CancellationToken cancellationToken,
        Func<Task<TResponse>> next)
    {
        var failures = _validators
            .Select(v => v.Validate(request))
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count == 0) return await next();

        // Serialize failures into { errors: { PropertyName: ["msg1","msg2"] } } payload
        var errorsByProperty = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToList());

        var payload = JsonSerializer.Serialize(new { errors = errorsByProperty });

        // If the pipeline expects a non-generic Result
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Fail(payload);
        }

        // If the pipeline expects Result<T>
        if (!typeof(TResponse).IsGenericType || typeof(TResponse).GetGenericTypeDefinition() != typeof(Result<>))
            throw new ValidationException(failures);

        var genericArg = typeof(TResponse).GenericTypeArguments[0];
        var resultGeneric = typeof(Result<>).MakeGenericType(genericArg);
        var failMethod = resultGeneric.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static);
        var failed = failMethod!.Invoke(null, new object[] { payload });
        return (TResponse)failed!;
    }
}
