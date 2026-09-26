namespace PulseFlow.Application.Validation;

/// <summary>Outcome of an <see cref="IRequestValidator{T}"/>.</summary>
public sealed class RequestValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<ValidationError> Errors { get; } = new();

    /// <summary>Adds an error and returns this instance, so rules can be chained.</summary>
    public RequestValidationResult AddError(string propertyName, string errorMessage)
    {
        Errors.Add(new ValidationError(propertyName, errorMessage));
        return this;
    }
}
