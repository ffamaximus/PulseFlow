using FluentValidation;
using PulseFlow.Application.Validation;

namespace PulseFlow.Tests;

// Both namespaces imported in the same file on purpose: this file only compiles if PulseFlow's validation types
// (IRequestValidator, RequestValidationResult, ValidationError) do not clash with FluentValidation's.

// PulseFlow validator (discovered by scanning; only runs when ValidationBehavior is registered).
public sealed class CreateOrderValidator : IRequestValidator<CreateOrder>
{
    public RequestValidationResult Validate(CreateOrder instance)
    {
        var result = new RequestValidationResult();
        if (string.IsNullOrWhiteSpace(instance.Name))
            result.AddError(nameof(CreateOrder.Name), "Name is required.");
        return result;
    }
}

// FluentValidation validator with an async rule (the 1.x behavior crashed on these).
public sealed class CreateOrderFluentValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderFluentValidator()
    {
        RuleFor(x => x.Name)
            .MustAsync(async (name, ct) =>
            {
                await Task.Yield();
                return name != "reserved";
            })
            .WithMessage("Name is reserved.");
    }
}
