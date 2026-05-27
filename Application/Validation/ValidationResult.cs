namespace PulseFlow.Application.Validation;

public class ValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<ValidationFailure> Errors { get; } = new();
}
