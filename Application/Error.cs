using PulseFlow.Application.Validation;

namespace PulseFlow.Application;

/// <summary>
/// A typed, immutable description of why an operation failed.
/// </summary>
public sealed record Error
{
    public Error(string code, string message, ErrorType type = ErrorType.Failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);

        Code = code;
        Message = message;
        Type = type;
    }

    /// <summary>Stable, machine-readable identifier, e.g. <c>"Order.NotFound"</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable description.</summary>
    public string Message { get; }

    public ErrorType Type { get; }

    /// <summary>Per-property errors when <see cref="Type"/> is <see cref="ErrorType.Validation"/>; otherwise empty.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = [];

    public static Error Failure(string code, string message) => new(code, message);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(
        IEnumerable<ValidationError> errors,
        string code = "Validation",
        string message = "One or more validation errors occurred.")
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new Error(code, message, ErrorType.Validation) { ValidationErrors = errors.ToArray() };
    }

    /// <summary>
    /// Groups <see cref="ValidationErrors"/> by property, in the shape expected by
    /// ASP.NET Core's <c>ValidationProblem</c> / <c>HttpValidationProblemDetails</c>.
    /// </summary>
    public IDictionary<string, string[]> ToValidationDictionary()
        => ValidationErrors
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

    public override string ToString() => $"{Code}: {Message}";
}
