![PulseFlow Banner](https://raw.githubusercontent.com/ffamaximus/PulseFlow/refs/heads/main/Banner2.png)
# PulseFlow

[![NuGet Version](https://img.shields.io/nuget/v/PulseFlow.svg?style=flat-square)](https://www.nuget.org/packages/PulseFlow/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![CI](https://github.com/ffamaximus/PulseFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/ffamaximus/PulseFlow/actions/workflows/ci.yml)

CQRS, mediator, typed results and DDD primitives for .NET 8, 9 and 10 — in one small, MIT-licensed package.

## Overview

`PulseFlow` gives you a mediator built specifically for CQRS: commands and queries are distinct concepts all the way
through the pipeline, every handler returns an explicit `Result` with a typed `Error`, and the DDD building blocks
(entities, aggregates, value objects, domain events) live next to it.

### Key Features
-   **Real CQRS semantics:** `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` and `IStreamQuery<TResponse>`, plus
    behaviors that run **only for commands** (transactions, idempotency) or **only for queries** (caching).
-   **Typed results:** `Result` / `Result<T>` with an `Error` (code, message, `ErrorType`, structured validation failures),
    implicit conversions, `Match`, `Map` and `Bind`.
-   **Low allocation:** `ValueTask` end to end, cached dispatch wrappers, and no delegate allocation when a request has no behaviors.
-   **Validation built in:** PulseFlow validators or FluentValidation (async rules supported), short-circuiting with a
    `Validation` error instead of exceptions.
-   **Notifications:** sequential (default), parallel or stop-on-exception publishing.
-   **DDD primitives:** `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `DomainEvent` and `IDomainEventDispatcher`.
-   **Modern .NET:** targets .NET 8, 9 and 10.
-   **Lean dependencies:** only `Microsoft.Extensions.DependencyInjection.Abstractions` and
    `Microsoft.Extensions.Logging.Abstractions`, at the lowest version of each target framework. Source Link and symbol packages included.

## Installation

```bash
dotnet add package PulseFlow

# Optional: FluentValidation integration
dotnet add package PulseFlow.FluentValidation
```

## Results and errors

```csharp
// Typed errors: code + message + ErrorType (Failure, Validation, NotFound, Conflict, Unauthorized, Forbidden)
var notFound = Error.NotFound("User.NotFound", "The user does not exist.");

Result ok = Result.Ok();                       // cached, no allocation
Result<int> value = 42;                        // implicit from the value
Result<int> failed = notFound;                 // implicit from an Error

if (failed.IsFailure)
    Console.WriteLine(failed.Error.Code);      // Error is non-null here (nullable analysis knows it)

var text = value
    .Map(x => x * 2)                           // Result<int> -> Result<int>
    .Bind(x => x > 0 ? Result<string>.Ok($"{x}") : Error.Validation("Value.Negative", "Must be positive"))
    .Match(v => $"value: {v}", e => $"error: {e.Code}");
```

`Result<T>.Value` throws on a failed result; check `IsSuccess` first or use `ValueOrDefault`.

Results serialize with System.Text.Json out of the box (and deserialize back, e.g. in an `HttpClient`):

```json
{ "isSuccess": true,  "value": { "id": 1, "email": "a@b.com" } }
{ "isSuccess": false, "error": { "code": "User.NotFound", "message": "The user does not exist.", "type": "NotFound", "validationErrors": [] } }
```

## Commands, queries and handlers

Handlers return `ValueTask<Result>` / `ValueTask<Result<T>>`. In `async` handlers you can `return value;` or
`return Error...;` directly thanks to the implicit conversions.

```csharp
// Command without response
public sealed record DeactivateUser(Guid Id) : ICommand;

public sealed class DeactivateUserHandler(IUserRepository users) : ICommandHandler<DeactivateUser>
{
    public async ValueTask<Result> Handle(DeactivateUser command, CancellationToken ct)
    {
        var user = await users.Find(command.Id, ct);
        if (user is null)
            return Error.NotFound("User.NotFound", "The user does not exist.");

        user.Deactivate();
        return Result.Ok();
    }
}

// Command with response (e.g. the id of the created entity)
public sealed record CreateUser(string Email) : ICommand<Guid>;

public sealed class CreateUserHandler(IUserRepository users) : ICommandHandler<CreateUser, Guid>
{
    public async ValueTask<Result<Guid>> Handle(CreateUser command, CancellationToken ct)
    {
        if (await users.Exists(command.Email, ct))
            return Error.Conflict("User.Duplicate", "The email is already registered.");

        var user = User.Create(command.Email);
        await users.Add(user, ct);
        return user.Id;
    }
}

// Query
public sealed record GetUser(Guid Id) : IQuery<UserDto>;

public sealed class GetUserHandler(IUserReadModel users) : IQueryHandler<GetUser, UserDto>
{
    public async ValueTask<Result<UserDto>> Handle(GetUser query, CancellationToken ct)
        => await users.Find(query.Id, ct) is { } user
            ? user
            : Error.NotFound("User.NotFound", "The user does not exist.");
}

// Stream query
public sealed record ExportUsers : IStreamQuery<UserDto>;
// ...handler implements IStreamQueryHandler<ExportUsers, UserDto> and returns IAsyncEnumerable<UserDto>
```

## Setup

```csharp
builder.Services.AddMediator(typeof(CreateUserHandler).Assembly);

// Optional: notification publish strategy (Sequential by default)
// builder.Services.AddMediator(o => o.PublishStrategy = PublishStrategy.Parallel, typeof(CreateUserHandler).Assembly);
```

`AddMediator` scans the given assemblies and registers every `ICommandHandler<>`, `ICommandHandler<,>`,
`IQueryHandler<,>`, `IStreamQueryHandler<,>`, `INotificationHandler<>`, `IDomainEventHandler<>` and `IRequestValidator<>`,
plus `IMediator` and `IDomainEventDispatcher`. It is safe to call more than once. Prefer passing assemblies explicitly:
the parameterless overload only scans assemblies already loaded. Open generic handler classes are skipped (register them manually).

```csharp
app.MapPost("/users", async (CreateUser command, IMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(command, ct);
    return result.Match(
        id => Results.Created($"/users/{id}", id),
        error => error.Type switch
        {
            ErrorType.Validation => Results.ValidationProblem(error.ToValidationDictionary()),
            ErrorType.NotFound => Results.NotFound(),
            ErrorType.Conflict => Results.Conflict(error.Message),
            _ => Results.Problem(error.Message)
        });
});

await foreach (var user in mediator.CreateStream(new ExportUsers(), ct)) { /* ... */ }
```

## Pipeline behaviors

There are three kinds of behavior, all with the same shape:

| Contract | Runs for | Register with |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | every command and query | `services.AddPipelineBehavior(typeof(X<,>))` |
| `ICommandPipelineBehavior<TCommand, TResponse>` | commands only | `services.AddCommandBehavior(typeof(X<,>))` |
| `IQueryPipelineBehavior<TQuery, TResponse>` | queries only | `services.AddQueryBehavior(typeof(X<,>))` |

Order: general behaviors (outermost, in registration order) → command/query behaviors → handler.
`TResponse` is always `Result` or `Result<T>`, so behaviors can use constraints instead of reflection:

```csharp
// Only for commands: wrap the handler in a transaction and commit only on success.
public sealed class TransactionBehavior<TCommand, TResponse>(AppDbContext db)
    : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : IBaseCommand
    where TResponse : Result
{
    public async ValueTask<TResponse> Handle(TCommand request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var response = await next();
        if (response.IsSuccess)
            await tx.CommitAsync(ct);
        return response;
    }
}

builder.Services.AddCommandBehavior(typeof(TransactionBehavior<,>));
```

To short-circuit with a failure, constrain `TResponse : IFailureFactory<TResponse>` and return
`TResponse.CreateFailure(error)`. Built-in behaviors: `ValidationBehavior<,>`, `FluentValidationBehavior<,>` (in `PulseFlow.FluentValidation`),
`ExceptionBehavior<,>` and `PerformanceBehavior<,>`.

## Validation

With PulseFlow validators (discovered by `AddMediator`):

```csharp
public sealed class CreateUserValidator : IRequestValidator<CreateUser>
{
    public RequestValidationResult Validate(CreateUser instance)
    {
        var result = new RequestValidationResult();
        if (string.IsNullOrWhiteSpace(instance.Email))
            result.AddError(nameof(CreateUser.Email), "Email is required.");
        return result;
    }
}

builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
```

With FluentValidation, through the `PulseFlow.FluentValidation` package (async rules such as `MustAsync` are supported):

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>(); // FluentValidation.DependencyInjectionExtensions
builder.Services.AddFluentValidationIntegration();
```

On failure the handler is not called and the caller receives a failed result whose `Error.Type` is
`ErrorType.Validation` and whose `Error.ValidationErrors` holds every failure (`Error.ToValidationDictionary()`
gives the shape expected by `Results.ValidationProblem`).

## Notifications

```csharp
public sealed record UserCreated(Guid Id) : INotification;

public sealed class SendWelcomeEmail : INotificationHandler<UserCreated>
{
    public ValueTask Handle(UserCreated notification, CancellationToken ct) => ValueTask.CompletedTask;
}

await mediator.Publish(new UserCreated(id), ct);
```

`PublishStrategy.Sequential` (default) runs every handler in order; `Parallel` runs them concurrently (do not use it
with a shared `DbContext`); `StopOnException` stops at the first failure.

## Domain primitives

```csharp
public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id) : base(id) { }
    public void Place() => AddDomainEvent(new OrderPlaced(Id));
}

public sealed record OrderPlaced(Guid OrderId) : DomainEvent;

public sealed class Email(string value) : ValueObject
{
    public string Value { get; } = value;
    protected override IEnumerable<object> GetEqualityComponents() { yield return Value; }
}

// Dispatch (e.g. after SaveChanges)
await dispatcher.DispatchAsync(order.DomainEvents, ct);
order.ClearEvents();
```

`ValueObject` equality compares the concrete type and the components; `==` and `!=` are provided.

## Migrating from 1.x

-   Handlers and behaviors return `ValueTask` instead of `Task`.
-   `IPipelineBehavior.Handle(request, next, ct)` — `next` is a `RequestHandlerDelegate<TResponse>` and comes before the token.
-   `Result.Error` is an `Error` instead of a `string`; `Result.Fail(string)` still works.
-   Streams: `mediator.CreateStream(query)` instead of `await mediator.Send(streamQuery)`.
-   Validation failures are returned as `Error.ValidationErrors` instead of a JSON string.
-   `IValidator<T>`, `ValidationResult` and `ValidationFailure` were renamed to `IRequestValidator<T>`, `RequestValidationResult`
    and `ValidationError` (no more name clashes with FluentValidation). `ValidationException` was removed.
-   `IRequestPreProcessor`, `IRequestPostProcessor` and `Unit` were removed (they were never invoked); pre/post processing
    will come back as a supported feature in a 2.x release.
-   FluentValidation support moved to the `PulseFlow.FluentValidation` package (`AddFluentValidationIntegration()` keeps its
    name and namespace); the core package no longer depends on FluentValidation.
-   `Publish` is sequential by default; `Result<T>.Value` throws on failure; `ValueObject` equality includes the type.

## Design Principles

-   **CQRS first:** commands and queries are different things, and the pipeline knows it.
-   **Explicit outcomes:** expected failures are `Result`s with typed `Error`s, not exceptions.
-   **Low overhead:** `ValueTask`, cached wrappers, allocation-free fast path.
-   **Testability & Composition:** small interfaces, easy to fake and compose.

## Roadmap

-   **Source generator:** reflection-free dispatch, Native AOT support and compile-time diagnostics (missing or duplicate handlers).
-   **ASP.NET Core integration:** `MapCommand` / `MapQuery` endpoints and automatic `Result` → `ProblemDetails` mapping.
-   **Pre/post processors, exception handlers and stream pipeline behaviors.**
-   **Observability:** OpenTelemetry traces and metrics.
-   **Domain events:** EF Core `SaveChanges` interceptor and outbox.

## Contributing

Contributions, issues, and pull requests are welcome! Please follow these guidelines:

-   Open an issue to discuss non-trivial changes before implementing.
-   Keep changes small and focused.
-   Add unit tests for new behavior and bug fixes.

## Support

This project is developed and maintained by **Andrés Mariño**. If you find this library useful and would like to support its continued development, you can buy me a coffee!

**Bitcoin (BTC):** `bc1p9zqgxghkjhauruhsza9n382e6kp5tpj4xtzu2csv4mypsdtdc4tqvdyg86`
[![Buy Me a Coffee at Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20Me-red?style=flat-square&logo=ko-fi)](https://ko-fi.com/andresdev21)

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
