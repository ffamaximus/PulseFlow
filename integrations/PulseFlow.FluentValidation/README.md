# PulseFlow.FluentValidation

[FluentValidation](https://fluentvalidation.net/) integration for [PulseFlow](https://www.nuget.org/packages/PulseFlow/).

```bash
dotnet add package PulseFlow.FluentValidation
dotnet add package FluentValidation.DependencyInjectionExtensions # for AddValidatorsFromAssemblyContaining
```

```csharp
builder.Services.AddMediator(typeof(CreateUser).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();
builder.Services.AddFluentValidationIntegration();
```

Every FluentValidation validator registered for a command or query runs before its handler, asynchronously
(rules such as `MustAsync` are supported). When validation fails the handler is not called and the caller receives a
failed `Result` whose `Error.Type` is `ErrorType.Validation` and whose `Error.ValidationErrors` lists every failure.
