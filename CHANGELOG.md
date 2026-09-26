# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased] - 2.1.0-preview.1

### Added
- New package **PulseFlow.AspNetCore**:
  - `MapCommand<TCommand>`, `MapCommand<TCommand, TResponse>` (optional `201 Created` with `Location`) and `MapQuery<TQuery, TResponse>` minimal API endpoints. POST/PUT/PATCH bind the JSON body; GET/DELETE bind route and query string. They return `RouteHandlerBuilder` and declare OpenAPI response metadata.
  - `Result.ToHttpResult()`, `Result<T>.ToHttpResult()`, `ToCreatedHttpResult(location)` and `Error.ToHttpResult()` for hand-written endpoints.
  - Errors become RFC 9457 ProblemDetails (`HttpValidationProblemDetails` for validation) with `errorCode` / `errorType` extensions, written through `IProblemDetailsService` when `AddProblemDetails()` is registered.
  - `AddPulseFlowHttp(options)` to customize the status code per `ErrorType` and the extensions.
- `IRequestPreProcessor<TRequest>` and `IRequestPostProcessor<TRequest, TResponse>` (`ValueTask`), run around the handler inside the behaviors. Closed implementations are discovered by `AddMediator`; open generic ones are registered with `AddRequestPreProcessor` / `AddRequestPostProcessor`.
- `IStreamPipelineBehavior<TRequest, TResponse>` and `StreamHandlerDelegate<TResponse>` for stream queries, registered with `AddStreamBehavior`.
- `MediatorOptions.HandlerLifetime` (default `Transient`) and `MediatorOptions.MediatorLifetime` (default `Scoped`).

### Changed
- Performance: requests whose pipeline has no behaviors or processors are remembered per container, so later calls skip resolving them from DI and go straight to the handler.

## [2.0.0-preview.2] - 2026-09-26

Upgrading from 1.x? See [MIGRATION.md](MIGRATION.md).

### Packaging
- New package **PulseFlow.FluentValidation** containing `FluentValidationBehavior` and `AddFluentValidationIntegration()` (same namespaces as before). The core package no longer depends on FluentValidation.
- Core dependencies reduced to `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`, at 8.0.0 / 9.0.0 / 10.0.0 per target framework (fixes NU1605 downgrade errors in apps pinned to 8.x or 9.x).
- Source Link, embedded untracked sources, `.snupkg` symbol packages, deterministic and CI builds, package validation across target frameworks.
- Shared package metadata in `Directory.Build.props`; repository URLs now point to github.com/ffamaximus/PulseFlow.
- GitHub Actions: `ci.yml` (build + tests on Linux and Windows for net8.0/net9.0/net10.0, packs on every push) and `release.yml` (publishes both packages to NuGet when a `v*` tag is pushed).

### Added
- `ICommand<TResponse>` / `ICommandHandler<TCommand, TResponse>`: commands that return a value (e.g. the id of a created entity).
- `IBaseCommand` / `IBaseQuery` markers, and CQRS-aware behaviors: `ICommandPipelineBehavior<,>` (commands only) and `IQueryPipelineBehavior<,>` (queries only). Registration helpers `AddPipelineBehavior`, `AddCommandBehavior`, `AddQueryBehavior`.
- `Error` record (`Code`, `Message`, `ErrorType`, `ValidationErrors`, `ToValidationDictionary()`) and `ErrorType` enum (serialized as a string).
- System.Text.Json converter for `Result` / `Result<T>` (`{ isSuccess, value | error }`), for both serialization and deserialization.
- `RequestValidationResult.AddError(property, message)`.
- `Result`: implicit conversions from `Error` and from `T`, `Match`, `Map`, `Bind`, nullable annotations (`Error` is non-null when `IsFailure`), cached `Result.Ok()`.
- `IFailureFactory<TSelf>` so behaviors can create failed results without reflection.
- `MediatorOptions` with `PublishStrategy` (`Sequential` by default, `Parallel`, `StopOnException`), configurable through the new `AddMediator(Action<MediatorOptions>, params Assembly[])` overload.
- `IDomainEventDispatcher` interface; `DispatchAsync` now accepts a `CancellationToken`.
- `Result<T>.ValueOrDefault`.
- Test project `tests/PulseFlow.Tests` (xUnit, net8.0/net9.0/net10.0).

### Changed
- **Breaking:** handlers, behaviors, notification and domain event handlers use `ValueTask` instead of `Task`; `IMediator.Send`/`Publish` return `ValueTask`.
- **Breaking:** `IPipelineBehavior.Handle(request, next, ct)` with `RequestHandlerDelegate<TResponse>` (same shape as MediatR / Mediator).
- **Breaking:** `Result.Error` is an `Error` instead of `string`. `Result.Fail(string)` is kept as a shortcut.
- **Breaking:** `IMediator.CreateStream(query)` returns `IAsyncEnumerable<T>` directly (replaces `Task<IAsyncEnumerable<T>> Send(streamQuery)`).
- **Breaking:** validation behaviors return `Error.Validation(failures)` instead of a JSON string, and require `Result`/`Result<T>` responses.
- **Breaking:** `IValidator<T>` → `IRequestValidator<T>`, `ValidationResult` → `RequestValidationResult`, `ValidationFailure` → `ValidationError` (record); `Error.ValidationFailures` → `Error.ValidationErrors`. Avoids ambiguous references when FluentValidation is imported in the same file.
- **Breaking (removed):** `IRequestPreProcessor`, `IRequestPostProcessor`, `Unit` and `ValidationException` were public but never used by the pipeline.
- `FluentValidationBehavior` uses the FluentValidation API directly and validates asynchronously (`MustAsync` rules no longer throw).
- No delegate allocation when a request has no behaviors.
- `ExceptionBehavior` no longer logs cancellations as errors; `PerformanceBehavior` also measures failed requests.
- **Breaking:** `Publish` runs handlers sequentially by default instead of in parallel. Every handler runs; one failure is rethrown as-is, several are thrown as `AggregateException`.
- **Breaking:** `Result<T>.Value` throws `InvalidOperationException` when the result is a failure (it used to return `default`). Serialization is handled by the new JSON converter, so a failed result serializes without touching `Value`.
- **Breaking:** `ValueObject` equality now also compares the concrete type; added `==`, `!=` and `IEquatable<ValueObject>`.
- `AddMediator` also scans `IStreamQueryHandler<,>`, `INotificationHandler<>` and `IDomainEventHandler<>`, registers `IDomainEventDispatcher`, uses `TryAdd*` (idempotent) and skips open generic handler classes.
- `DomainEventDispatcher` uses cached typed wrappers instead of `dynamic`.

### Fixed
- `Publish` with `Parallel`: a handler throwing synchronously prevented the remaining handlers from starting.
- Notification and stream query handlers were never registered by `AddMediator`.
- `DomainEventDispatcher` failed with `internal` handlers or explicit interface implementations.
- Command, query and stream wrappers shared one cache keyed only by request type.
- `Send(IStreamQuery)` was declared `async` without awaiting (CS1998).

## [1.1.0] - 2026-05-28

### Added
- Public overload `AddMediator(this IServiceCollection, params Assembly[]?)` that accepts assemblies to scan (previously the overload was private). This allows consumers to call `services.AddMediator(Assembly.GetExecutingAssembly())` from `Program.cs`.
- Automatic discovery and registration of `IValidator<>` implementations when using `AddMediator(Assembly...)`, so validators discovered in scanned assemblies are registered into DI and can be resolved by `ValidationBehavior`.

### Changed
- Documentation: Updated README to show how to register `ValidationBehavior` as an `IPipelineBehavior<,>` and to document the `AddMediator(Assembly...)` overload and the automatic validator discovery behavior.

### Fixed
- Adjusted `ServiceCollectionExtensions` visibility and registration logic so that pipeline behaviors and validators can be resolved properly at runtime.

## [1.0.0] - 2026-05-13

### Added
- Support for Stream Queries: Added `IStreamQuery<TResponse>`, `IStreamQueryHandler<TQuery, TResponse>`, and `Send<TResponse>(IStreamQuery<TResponse>)` method in `IMediator` and `Mediator` for handling asynchronous data streams.
- Dependency on `Microsoft.Extensions.Logging` for improved logging in behaviors.

### Changed
- Optimized notification publishing: `PublishWrapper` now executes handlers in parallel using `Task.WhenAll` for better performance when multiple handlers are present.
- Updated `ExceptionBehavior` and `PerformanceBehavior` to use `ILogger` instead of `Console.WriteLine` for structured logging.
- Improved namespace consistency in behavior classes.

### Fixed
- Corrected compilation issues related to stream query pipeline handling.

### Notes
- Stream queries do not currently apply pipeline behaviors to simplify implementation. Behaviors can be added in future versions if needed.


