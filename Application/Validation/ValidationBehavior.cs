using PulseFlow.Application.Mediator;

namespace PulseFlow.Application.Validation;

/// <summary>
/// Runs every registered <see cref="IRequestValidator{T}"/> for the request. On failure the handler is not called and a
/// failed result carrying <see cref="Error.Validation(IEnumerable{ValidationError},string,string)"/> is returned.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TResponse : IFailureFactory<TResponse>
{
    private readonly IRequestValidator<TRequest>[] _validators;

    public ValidationBehavior(IEnumerable<IRequestValidator<TRequest>> validators)
    {
        _validators = validators as IRequestValidator<TRequest>[] ?? validators.ToArray();
    }

    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
            return next();

        List<ValidationError>? failures = null;
        foreach (var validator in _validators)
        {
            var result = validator.Validate(request);
            if (!result.IsValid)
                (failures ??= new List<ValidationError>()).AddRange(result.Errors);
        }

        return failures is null
            ? next()
            : ValueTask.FromResult(TResponse.CreateFailure(Error.Validation(failures)));
    }
}
