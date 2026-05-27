# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-05-13

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
