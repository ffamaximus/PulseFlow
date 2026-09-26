using PulseFlow.Application.Mediator;
using FV = FluentValidation;

namespace PulseFlow.Application.Validation;

/// <summary>
/// Runs every FluentValidation validator registered for the request, asynchronously (rules such as
/// <c>MustAsync</c> are supported). On failure the handler is not called and a failed result carrying
/// <see cref="Error.Validation(IEnumerable{ValidationError},string,string)"/> is returned.
/// </summary>
public class FluentValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TResponse : IFailureFactory<TResponse>
{
    private readonly FV.IValidator<TRequest>[] _validators;

    public FluentValidationBehavior(IEnumerable<FV.IValidator<TRequest>> validators)
    {
        _validators = validators as FV.IValidator<TRequest>[] ?? validators.ToArray();
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Length == 0)
            return await next().ConfigureAwait(false);

        List<ValidationError>? failures = null;
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            foreach (var error in result.Errors)
                (failures ??= new List<ValidationError>()).Add(new ValidationError(error.PropertyName, error.ErrorMessage));
        }

        return failures is null
            ? await next().ConfigureAwait(false)
            : TResponse.CreateFailure(Error.Validation(failures));
    }
}
