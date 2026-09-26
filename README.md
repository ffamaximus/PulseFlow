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
-   **Observability built in:** OpenTelemetry-ready traces and metrics for every request, zero cost when disabled.
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

# Optional integrations
dotnet add package PulseFlow.AspNetCore        # minimal API endpoints + ProblemDetails
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

// Optional settings
builder.Services.AddMediator(o =>
{
    o.PublishStrategy = PublishStrategy.Parallel;          // default: Sequential
    o.HandlerLifetime = ServiceLifetime.Scoped;            // default: Transient
    o.MediatorLifetime = ServiceLifetime.Transient;        // default: Scoped
}, typeof(CreateUserHandler).Assembly);
```

`AddMediator` scans the given assemblies and registers every `ICommandHandler<>`, `ICommandHandler<,>`,
`IQueryHandler<,>`, `IStreamQueryHandler<,>`, `INotificationHandler<>`, `IDomainEventHandler<>` and `IRequestValidator<>`,
plus `IMediator` (also exposed as `ISender` for commands/queries and `IPublisher` for notifications) and
`IDomainEventDispatcher`. It is safe to call more than once. Prefer passing assemblies explicitly:
the parameterless overload only scans assemblies already loaded. Open generic handler classes are skipped (register them manually).

```csharp
var result = await mediator.Send(new CreateUser("a@b.com"), ct);          // Result<Guid>
await foreach (var user in mediator.CreateStream(new ExportUsers(), ct)) { /* ... */ }
```

## ASP.NET Core

With the `PulseFlow.AspNetCore` package, commands and queries become minimal API endpoints in one line, and every
`Error` becomes an RFC 9457 ProblemDetails response with the right status code:

```csharp
using PulseFlow.AspNetCore;

builder.Services.AddProblemDetails();   // optional, recommended

app.MapCommand<CreateUser, Guid>("/users", id => $"/users/{id}");   // POST (JSON body) -> 201 Created + Location
app.MapCommand<DeactivateUser>("/users/{id}", HttpMethods.Delete);  // DELETE (route)     -> 204 No Content
app.MapQuery<GetUser, UserDto>("/users/{id}");                       // GET (route/query)  -> 200 OK

// Hand-written endpoints use the same mapping:
app.MapPut("/users/{id}", async (Guid id, RenameBody body, IMediator mediator, CancellationToken ct) =>
    (await mediator.Send(new RenameUser(id, body.Name), ct)).ToHttpResult());
```

| `ErrorType` | Status | Body |
|---|---|---|
| `Validation` | 400 | `HttpValidationProblemDetails` with `errors` per property |
| `Failure` | 400 | ProblemDetails |
| `NotFound` / `Conflict` | 404 / 409 | ProblemDetails |
| `Unauthorized` / `Forbidden` | 401 / 403 | ProblemDetails |

Every ProblemDetails carries `errorCode` and `errorType` extensions. Customize with
`builder.Services.AddPulseFlowHttp(o => o.StatusCodeMap[ErrorType.Validation] = 422)`. Endpoints return the
`RouteHandlerBuilder`, so `.RequireAuthorization()`, `.WithName()`, `.WithTags()` and OpenAPI metadata work as usual.

## Pipeline behaviors

There are three kinds of behavior, all with the same shape:

| Contract | Runs for | Register with |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | every command and query | `services.AddPipelineBehavior(typeof(X<,>))` (optionally with a `ServiceLifetime`) |
| `ICommandPipelineBehavior<TCommand, TResponse>` | commands only | `services.AddCommandBehavior(typeof(X<,>))` |
| `IQueryPipelineBehavior<TQuery, TResponse>` | queries only | `services.AddQueryBehavior(typeof(X<,>))` |
| `IRequestPreProcessor<TRequest>` | before the handler | closed types: scanned; open: `services.AddRequestPreProcessor(typeof(X<>))` |
| `IRequestPostProcessor<TRequest, TResponse>` | after the handler (also on failed results) | closed types: scanned; open: `services.AddRequestPostProcessor(typeof(X<,>))` |
| `IStreamPipelineBehavior<TRequest, TResponse>` | stream queries | `services.AddStreamBehavior(typeof(X<,>))` |
| `IRequestExceptionHandler<TRequest, TResponse>` | when the handler throws: return a response instead (e.g. a typed `Error`) or let it propagate | closed types: scanned; open: `services.AddRequestExceptionHandler(typeof(X<,>))` |

Order: general behaviors (outermost, in registration order) → command/query behaviors → pre-processors → handler →
post-processors. Pipelines without behaviors or processors cost nothing: PulseFlow remembers per container which
requests have an empty pipeline and calls their handler directly.
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

## Observability (OpenTelemetry)

PulseFlow emits a span and a duration measurement for every command, query, notification and stream, using the
standard .NET `ActivitySource` and `Meter` APIs (no extra package). Turn them on in your OpenTelemetry setup:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(PulseFlowDiagnostics.ActivitySourceName))   // "PulseFlow"
    .WithMetrics(m => m.AddMeter(PulseFlowDiagnostics.MeterName));            // "PulseFlow"
```

| Signal | Name | Tags |
|---|---|---|
| Span | `command CreateOrder`, `query GetOrder`, `notification OrderPlaced`, `stream ExportOrders` | `pulseflow.request.kind`, `pulseflow.request.type`, `pulseflow.outcome`, `error.type`, `pulseflow.error.code`, `pulseflow.stream.items` |
| Histogram (s) | `pulseflow.request.duration` | `pulseflow.request.kind`, `pulseflow.request.type`, `pulseflow.outcome`, `error.type` |

`pulseflow.outcome` is `success`, `failure` (a failed `Result`; `error.type` holds its `ErrorType`), `exception`
(the span is marked as error) or `incomplete` (a stream that was not fully consumed). Spans nest under the current
activity (for example the incoming HTTP request), and handler work shows up as their children. When nothing listens,
the mediator keeps its uninstrumented fast path.

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

Step-by-step guide with before/after examples: **[MIGRATION.md](MIGRATION.md)**. Summary:

-   Handlers and behaviors return `ValueTask` instead of `Task`.
-   `IPipelineBehavior.Handle(request, next, ct)` — `next` is a `RequestHandlerDelegate<TResponse>` and comes before the token.
-   `Result.Error` is an `Error` instead of a `string`; `Result.Fail(string)` still works.
-   Streams: `mediator.CreateStream(query)` instead of `await mediator.Send(streamQuery)`.
-   Validation failures are returned as `Error.ValidationErrors` instead of a JSON string.
-   `IValidator<T>`, `ValidationResult` and `ValidationFailure` were renamed to `IRequestValidator<T>`, `RequestValidationResult`
    and `ValidationError` (no more name clashes with FluentValidation). `ValidationException` was removed.
-   `IRequestPreProcessor` / `IRequestPostProcessor` were redesigned (`ValueTask`, and now actually invoked by the pipeline);
    `Unit` was removed.
-   FluentValidation support moved to the `PulseFlow.FluentValidation` package (`AddFluentValidationIntegration()` keeps its
    name and namespace); the core package no longer depends on FluentValidation.
-   `Publish` is sequential by default; `Result<T>.Value` throws on failure; `ValueObject` equality includes the type.

## Benchmarks

`benchmarks/PulseFlow.Benchmarks` compares PulseFlow with MediatR 14 and Mediator 3 (source generator) on the same
workloads, each library with its default configuration. Latest run (2.1.0-preview.2, Ryzen 5 3600, .NET 10):

| Scenario | PulseFlow | MediatR | Mediator (source gen) |
|---|---|---|---|
| Send | **39 ns** · 56 B | 71 ns · 128 B | 11 ns · 0 B |
| Send + 2 behaviors | **120 ns** · 384 B (singleton behaviors: **88 ns** · 272 B) | 159 ns · 512 B | 18 ns · 0 B |
| Publish to 2 handlers | **65 ns** · 88 B | 326 ns · 1,008 B | 14 ns · 0 B |
| Stream 10 items | **200 ns** · 208 B | 485 ns · 576 B | 181 ns · 184 B |
| Cold start (new process, register + first send) | **46 ms** · 39 KB | 78 ms · 284 KB | 37 ms · 40 KB |

- Faster than MediatR, with less memory, in every scenario: 1.8x on send, 1.3–1.8x with behaviors, 5x on publish,
  2.4x on streams and 1.7x on cold start. Stateless behaviors can be registered as singletons
  (`AddPipelineBehavior(type, ServiceLifetime.Singleton)`) for the fastest pipeline.
- Mediator is faster: it generates the dispatch code at compile time. Closing that gap is the goal of PulseFlow 3.0.
- The numbers are the mediator's own overhead (handlers do no work). In a request that queries a database (1–5 ms)
  the difference between any of the three is well below 0.01%.

Full tables, methodology and how to run them: [benchmarks/README.md](benchmarks/README.md).

## PulseFlow or Mediator?

[Mediator](https://github.com/martinothamar/Mediator) is an excellent library and it is faster: it generates the
dispatch code at compile time (see the benchmarks above). PulseFlow makes a different trade-off. It is not the fastest
mediator; it is the one that covers the most of a CQRS application with the least glue code, while staying faster
than MediatR.

**What PulseFlow gives you that Mediator does not:**

- **Typed outcomes.** Handlers return `Result` / `Result<T>` with an `Error` (code, message, `ErrorType`,
  validation errors), `Match` / `Map` / `Bind` and JSON support. Mediator returns whatever the handler returns; how
  failures are represented is left to each application.
- **Validation in the pipeline.** Validators run before the handler and a failure comes back as a typed
  `Validation` error, never as an exception (PulseFlow validators or FluentValidation with async rules).
- **ASP.NET Core in one line.** `MapCommand` / `MapQuery` endpoints and automatic `Error` → RFC 9457 ProblemDetails
  with the right status code (`PulseFlow.AspNetCore`). With Mediator every endpoint maps results by hand.
- **Telemetry that knows about business failures.** Spans and metrics tag the outcome as `success`, `failure` (with
  the `ErrorType`) or `exception`, so a `NotFound` does not look like a crash in your dashboards. This is possible
  because PulseFlow understands the `Result` a handler returns.
- **CQRS-specific behaviors.** `ICommandPipelineBehavior` and `IQueryPipelineBehavior` with registration helpers,
  so "transactions only for commands" or "caching only for queries" is one line.
- **DDD building blocks.** `Entity`, `AggregateRoot`, `ValueObject` and a domain event dispatcher in the same package.
- **Plain reflection, no source generator.** Handlers can live in any project of the solution and there is no
  generated code or generator placement rule to manage. Scoped handlers by default, safe with an EF Core `DbContext`.

**Choose Mediator instead when:**

- dispatch cost is on your hot path (hundreds of thousands of in-process messages per second);
- you need **Native AOT** or trimming, or compile-time errors for missing handlers (PulseFlow's source generator is
  planned for 3.0);
- maturity matters most: Mediator has millions of downloads and a long track record.

For a typical web API, the dispatch difference (tens of nanoseconds) disappears next to a single database call, and
the features above are what save time every day.

## Design Principles

-   **CQRS first:** commands and queries are different things, and the pipeline knows it.
-   **Explicit outcomes:** expected failures are `Result`s with typed `Error`s, not exceptions.
-   **Low overhead:** `ValueTask`, cached wrappers, allocation-free fast path.
-   **Testability & Composition:** small interfaces, easy to fake and compose.

## Roadmap

-   **Source generator:** reflection-free dispatch, Native AOT support and compile-time diagnostics (missing or duplicate handlers).
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
