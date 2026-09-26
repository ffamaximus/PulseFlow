namespace PulseFlow.Application.Validation;

/// <summary>A single validation failure for one property of a request.</summary>
public sealed record ValidationError(string PropertyName, string ErrorMessage);
