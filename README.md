![PulseFlow Banner](https://raw.githubusercontent.com/ffamaximus/PulseFlow/refs/heads/main/Banner2.png)
                    https://raw.githubusercontent.com/ffamaximus/PulseFlow/refs/heads/main/Banner2.png
# PulseFlow

[![NuGet Version](https://img.shields.io/nuget/v/PulseFlow.svg?style=flat-square)](https://www.nuget.org/packages/PulseFlow/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

Lightweight, dependency-free primitives for implementing Clean Architecture, Domain-Driven Design (DDD), and CQRS in .NET applications.

## Overview

`PulseFlow  ` provides a small, focused set of abstractions designed to simplify the modeling of domain-centered applications without introducing infrastructure or framework dependencies. It aims to be a high-performance, foundational library for building robust and scalable systems.

### Key Features:
-   **Minimal & Focused:** A small, well-documented API surface.
-   **Framework-Agnostic:** Suitable for various application types (web, serverless, desktop, worker apps).
-   **Dependency-Free:** No external runtime dependencies, ensuring a lean footprint.
-   **High Performance:** Optimized Mediator implementation using wrapper patterns and caching for efficient command/query dispatch.
-   **Testability:** Designed for easy unit testing and composition.
-   **Modern .NET:** Compatible with .NET 8 and later.

### Included Primitives:
-   `Result` / `Result<T>`: For explicit error handling and success/failure representation.
-   `Entity<TId>`: Base class for domain entities with identity.
-   `AggregateRoot<TId>`: Base class for DDD aggregate roots, extending `Entity`.
-   `ValueObject`: Base class for implementing value objects.
-   `DomainEvent`: Base class for domain events.
-   `ICommand`, `IQuery`, `INotification`: Interfaces for CQRS messages.
-   `ICommandHandler<TCommand>`, `IQueryHandler<TQuery, TResult>`, `INotificationHandler<TNotification>`: Interfaces for handling CQRS messages.
-   `IMediator`: Interface for dispatching commands, queries, and notifications.
-   `IPipelineBehavior<TRequest, TResponse>`: For cross-cutting concerns in the Mediator pipeline.

These building blocks are intentionally minimal and designed to be composed into larger architectures (microservices, monoliths, event-driven systems, serverless functions, etc.).

## Installation

Install the package from NuGet:

```bash
dotnet add package PulseFlow
```

## Quick Examples

### Result Usage:

```csharp
var result = Result.Ok();
if (result.IsFailure) Console.WriteLine(result.Error);

var userResult = Result<string>.Ok("User created successfully!");
```

### Entity / AggregateRoot:

```csharp
public class User : Entity<Guid>
{
    public string Email { get; }

    public User(Guid id, string email) : base(id)
    {
        Email = email;
    }
}

public class Order : AggregateRoot<Guid>
{
    public Order(Guid id) : base(id) { }
}
```

### Value Object:

```csharp
public class Email : ValueObject
{
    public string Value { get; }

    public Email(string value) => Value = value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

### Commands / Queries and Handlers:

```csharp
// Define a Command and its Handler
public record CreateUserCommand(string Email) : ICommand;

public class CreateUserHandler : ICommandHandler<CreateUserCommand>
{
    public Task<Result> Handle(CreateUserCommand command, CancellationToken ct)
    {
        // Domain logic to create a user...
        Console.WriteLine($"Creating user with email: {command.Email}");
        return Task.FromResult(Result.Ok());
    }
}

// Define a Query and its Handler
public record GetUserQuery(Guid Id) : IQuery<User>;

public class GetUserHandler : IQueryHandler<GetUserQuery, User>
{
    public Task<Result<User>> Handle(GetUserQuery query, CancellationToken ct)
    {
        // Logic to retrieve a user...
        var user = new User(query.Id, "test@mail.com");
        return Task.FromResult(Result<User>.Ok(user));
    }
}
```

### Mediator Usage and Setup:

First, register the Mediator and your handlers in your `Program.cs` or `Startup.cs`:

```csharp
using PulseFlow.Application.Mediator;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Register Mediator and scan for handlers in the current assembly
        builder.Services.AddMediator(Assembly.GetExecutingAssembly());

        // Or to scan all loaded assemblies:
        // builder.Services.AddMediator();

        var app = builder.Build();

        // Example usage:
        app.Run(async (context) =>
        {
            var mediator = context.RequestServices.GetRequiredService<IMediator>();

            // Sending a Command
            var commandResult = await mediator.Send(new CreateUserCommand("new.user@example.com"));
            if (commandResult.IsSuccess)
            {
                await context.Response.WriteAsync("Command executed successfully!");
            }
            else
            {
                await context.Response.WriteAsync($"Command failed: {commandResult.Error}");
            }

            // Sending a Query
            var queryResult = await mediator.Send(new GetUserQuery(Guid.NewGuid()));
            if (queryResult.IsSuccess)
            {
                await context.Response.WriteAsync($"\nQuery executed successfully! User: {queryResult.Value.Email}");
            }
            else
            {
                await context.Response.WriteAsync($"\nQuery failed: {queryResult.Error}");
            }
        });

        app.Run();
    }
}
```

## Design Principles

-   **Single-Responsibility:** Small, focused API surface for each primitive.
-   **No External Dependencies:** Ensures a lightweight and flexible core.
-   **Predictable Behavior:** Explicit `Result` types for clear success/failure outcomes.
-   **High Performance:** Optimized Mediator dispatch for minimal overhead.
-   **Testability & Composition:** Designed for easy unit testing and integration into complex systems.

## Roadmap

-   **Pipeline Behaviors:** Built-in support for logging, validation, and metrics.
-   **Async Domain Event Dispatcher:** Enhanced event handling capabilities.
-   **Notification Pattern:** Improved handling of side effects and notifications.
-   **Optional Integrations:** (e.g., Redis, Kafka, RabbitMQ) for advanced scenarios.
-   **Source Generators:** To further reduce boilerplate code.

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
