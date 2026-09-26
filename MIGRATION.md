# Migrating from PulseFlow 1.x to 2.x

PulseFlow 2.0 is a major release. Most changes are caught by the compiler; a few change behavior silently and are
listed in [Silent behavior changes](#silent-behavior-changes) — review those even if your project compiles.

Typical effort: 1–2 hours for an average project, mostly mechanical (`Task` → `ValueTask`, reading `Error`).

## Checklist

- [ ] Update the packages (see [Packages](#1-packages)).
- [ ] Handlers: `Task` → `ValueTask` (see [2](#2-handlers-task--valuetask)).
- [ ] Pipeline behaviors: new signature (see [3](#3-pipeline-behaviors)).
- [ ] `Result.Error` is now an `Error` object (see [4](#4-result-and-error)).
- [ ] Streams: `CreateStream` (see [5](#5-stream-queries)).
- [ ] Validators renamed; FluentValidation in its own package (see [6](#6-validation)).
- [ ] Remove removed APIs (see [7](#7-removed-apis)).
- [ ] Review the [silent behavior changes](#silent-behavior-changes).

---

## 1. Packages

```bash
dotnet add package PulseFlow --prerelease

# Only if you use FluentValidation:
dotnet add package PulseFlow.FluentValidation --prerelease
```

PulseFlow now depends only on `Microsoft.Extensions.DependencyInjection.Abstractions` and
`Microsoft.Extensions.Logging.Abstractions`. It no longer brings in transitively:

| No longer transitive | Who is affected | Fix |
|---|---|---|
| `FluentValidation` | Projects that used FluentValidation only through PulseFlow | `dotnet add package FluentValidation` (or `PulseFlow.FluentValidation`) |
| `Microsoft.Extensions.DependencyInjection` | Console apps, workers or tests using `new ServiceCollection().BuildServiceProvider()` | `dotnet add package Microsoft.Extensions.DependencyInjection` |
| `Microsoft.Extensions.Logging` | Console apps, workers or tests using `AddLogging()` | `dotnet add package Microsoft.Extensions.Logging` |

ASP.NET Core apps are not affected by the last two (the shared framework already includes them).

## 2. Handlers: `Task` → `ValueTask`

Applies to command, query, notification and domain event handlers.

**1.x**
```csharp
public class CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public Task<Result> Handle(CreateUserCommand command, CancellationToken ct)
        => Task.FromResult(Result.Ok());
}

public class GetUserHandler : IQueryHandler<GetUserQuery, User>
{
    public async Task<Result<User>> Handle(GetUserQuery query, CancellationToken ct)
        => Result<User>.Ok(await _repo.Find(query.Id, ct));
}

public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken ct) => Task.CompletedTask;
}
```

**2.x**
```csharp
public class CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public ValueTask<Result> Handle(CreateUserCommand command, CancellationToken ct)
        => ValueTask.FromResult(Result.Ok());
}

public class GetUserHandler : IQueryHandler<GetUserQuery, User>
{
    public async ValueTask<Result<User>> Handle(GetUserQuery query, CancellationToken ct)
        => await _repo.Find(query.Id, ct);            // implicit User -> Result<User>
}

public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    public ValueTask Handle(UserCreated notification, CancellationToken ct) => ValueTask.CompletedTask;
}
```

Find & replace that covers most cases (review each match):

| Find | Replace |
|---|---|
| `Task<Result>` | `ValueTask<Result>` |
| `Task<Result<` | `ValueTask<Result<` |
| `Task.FromResult(Result` | `ValueTask.FromResult(Result` |
| `Task.CompletedTask` (in notification / domain event handlers) | `ValueTask.CompletedTask` |

Also:

- `IDomainEventHandler<T>.Handle` returns `ValueTask` and the `CancellationToken` is no longer optional.
- Callers: `IMediator.Send` / `Publish` return `ValueTask`. `await mediator.Send(...)` keeps working; if you stored the
  task (`var t = mediator.Send(...)`) await it only once, or call `.AsTask()` if you need `Task.WhenAll`.

**New (optional):** commands can now return a value, so you can stop using queries for writes:

```csharp
public record CreateOrder(string Name) : ICommand<Guid>;

public class CreateOrderHandler : ICommandHandler<CreateOrder, Guid>
{
    public async ValueTask<Result<Guid>> Handle(CreateOrder command, CancellationToken ct)
    {
        // ...
        return order.Id;
    }
}
```

## 3. Pipeline behaviors

The parameter order changed (now the same as MediatR / Mediator) and `next` is a `RequestHandlerDelegate<TResponse>`.

**1.x**
```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, CancellationToken ct, Func<Task<TResponse>> next)
    {
        var response = await next();
        return response;
    }
}

services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

**2.x**
```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();
        return response;
    }
}

services.AddPipelineBehavior(typeof(LoggingBehavior<,>));   // AddTransient(...) still works
```

If your behavior used reflection to build a failed `Result` / `Result<T>`, replace it with a constraint:

```csharp
public class MyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TResponse : IFailureFactory<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        => IsAllowed(request)
            ? next()
            : ValueTask.FromResult(TResponse.CreateFailure(Error.Forbidden("Access.Denied", "Not allowed.")));
}
```

Use `where TResponse : Result` to read `IsSuccess` / `Error` of the response. Behaviors that should run only for commands
(transactions) or only for queries (caching) can now implement `ICommandPipelineBehavior<,>` /
`IQueryPipelineBehavior<,>` and be registered with `AddCommandBehavior` / `AddQueryBehavior`.

## 4. `Result` and `Error`

`Result.Error` changed from `string?` to `Error?` (`Code`, `Message`, `Type`, `ValidationErrors`).

**1.x**
```csharp
return Result.Fail("User not found");

if (result.IsFailure)
    logger.LogWarning(result.Error);
```

**2.x**
```csharp
return Result.Fail("User not found");                                   // still compiles (Code = "Failure")
return Error.NotFound("User.NotFound", "User not found");               // recommended: typed error

if (result.IsFailure)
    logger.LogWarning("{Code}: {Message}", result.Error.Code, result.Error.Message);
```

| 1.x | 2.x |
|---|---|
| `result.Error` (string) | `result.Error.Message` |
| `Result.Fail("msg")` | unchanged, or `Error.Failure/NotFound/Conflict/Validation/Unauthorized/Forbidden(code, msg)` |
| `Result<T>.Fail("msg")` | unchanged, or `return Error.NotFound(...)` (implicit conversion) |
| `Result<T>.Ok(value)` | unchanged, or `return value` (implicit conversion) |
| `result.Value` on a failed result → `default` | **throws** — use `IsSuccess` first or `ValueOrDefault` |

Mapping to HTTP now works on the error type instead of parsing messages:

```csharp
return result.Match(
    value => Results.Ok(value),
    error => error.Type switch
    {
        ErrorType.Validation => Results.ValidationProblem(error.ToValidationDictionary()),
        ErrorType.NotFound => Results.NotFound(),
        ErrorType.Conflict => Results.Conflict(error.Message),
        _ => Results.Problem(error.Message)
    });
```

## 5. Stream queries

**1.x**
```csharp
var stream = await mediator.Send(new ExportUsers());
await foreach (var user in stream) { }
```

**2.x**
```csharp
await foreach (var user in mediator.CreateStream(new ExportUsers(), ct)) { }
```

## 6. Validation

### PulseFlow validators (renamed to avoid clashes with FluentValidation)

| 1.x | 2.x |
|---|---|
| `IValidator<T>` | `IRequestValidator<T>` |
| `ValidationResult` | `RequestValidationResult` |
| `ValidationFailure` (class) | `ValidationError` (record) |
| `result.Errors.Add(new ValidationFailure(p, m))` | `result.AddError(p, m)` |

**1.x**
```csharp
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public ValidationResult Validate(CreateUserCommand command)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(command.Email))
            result.Errors.Add(new ValidationFailure("Email", "Email is required."));
        return result;
    }
}
```

**2.x**
```csharp
public class CreateUserValidator : IRequestValidator<CreateUserCommand>
{
    public RequestValidationResult Validate(CreateUserCommand command)
    {
        var result = new RequestValidationResult();
        if (string.IsNullOrWhiteSpace(command.Email))
            result.AddError("Email", "Email is required.");
        return result;
    }
}
```

### Reading validation errors

In 1.x the errors were a JSON string inside `Result.Error` (`{"errors":{"Email":["..."]}}`). In 2.x they are structured:

**1.x**
```csharp
var payload = JsonSerializer.Deserialize<ErrorsPayload>(result.Error!);
```

**2.x**
```csharp
if (result.Error?.Type == ErrorType.Validation)
{
    foreach (var e in result.Error.ValidationErrors)
        Console.WriteLine($"{e.PropertyName}: {e.ErrorMessage}");

    var byProperty = result.Error.ToValidationDictionary();   // IDictionary<string, string[]>
}
```

### FluentValidation

Moved to the `PulseFlow.FluentValidation` package. Code does not change (same method and namespaces), and async rules
(`MustAsync`) now work:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();
builder.Services.AddFluentValidationIntegration();
```

## 7. Removed APIs

These were public in 1.x but the pipeline never invoked them:

| Removed | Replacement |
|---|---|
| `IRequestPreProcessor<TRequest>` (Task-based) | Back in 2.1 with `ValueTask` and actually invoked: `ValueTask Process(TRequest, CancellationToken)` |
| `IRequestPostProcessor<TRequest, TResponse>` (Task-based) | Back in 2.1 with `ValueTask` and actually invoked: `ValueTask Process(TRequest, TResponse, CancellationToken)` |
| `Unit` | `ICommand` (no response) returns `Result` |
| `PulseFlow.Application.Validation.ValidationException` | Validation failures are returned as `Error.Validation(...)`, not thrown |

`DomainEventDispatcher.DispatchAsync` now takes an optional `CancellationToken`, and there is an
`IDomainEventDispatcher` interface (registered by `AddMediator`) that you should inject instead of the class.

---

## Silent behavior changes

These compile without errors but behave differently. Review them before deploying.

### Handlers registered twice

`AddMediator(assemblies)` now also registers `INotificationHandler<>`, `IStreamQueryHandler<,>`,
`IDomainEventHandler<>` and `ICommandHandler<,>`. If you registered any of them **manually after** `AddMediator`,
they are now registered twice and **run twice**.

```csharp
services.AddMediator(typeof(Program).Assembly);
services.AddTransient<INotificationHandler<UserCreated>, SendWelcomeEmail>();   // remove this line
```

Search your startup code for `INotificationHandler<`, `IDomainEventHandler<` and `IStreamQueryHandler<` registrations and
remove them. (Manual registrations made *before* `AddMediator` are not duplicated.)

### `Publish` is sequential by default

1.x ran notification handlers in parallel (`Task.WhenAll`), which broke with a shared scoped `DbContext`. 2.x runs them
one after another; every handler runs even if one fails (one failure is rethrown as-is, several as `AggregateException`).
To keep the 1.x behavior:

```csharp
services.AddMediator(o => o.PublishStrategy = PublishStrategy.Parallel, typeof(Program).Assembly);
```

### `Result<T>.Value` throws on failure

1.x returned `default` (e.g. `null` or `0`). 2.x throws `InvalidOperationException`. Search for `.Value` usages that are
not guarded by `IsSuccess`, or switch them to `ValueOrDefault`.

### `ValueObject` equality includes the concrete type

`new Email("a") == new Username("a")` was `true` in 1.x and is `false` in 2.x. `==` and `!=` are now overloaded (1.x
compared references). Check `HashSet`/`Dictionary` keys and comparisons between value objects.

### JSON shape of `Result`

If an API returned a `Result` / `Result<T>` directly, the payload is now:

```json
{ "isSuccess": true, "value": { } }
{ "isSuccess": false, "error": { "code": "User.NotFound", "message": "...", "type": "NotFound", "validationErrors": [] } }
```

`error` used to be a string. Update the clients that read it.

---

Questions or problems migrating? Open an issue at https://github.com/ffamaximus/PulseFlow/issues.
