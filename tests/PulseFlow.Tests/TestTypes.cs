using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PulseFlow.Application;
using PulseFlow.Application.Commands;
using PulseFlow.Application.Mediator;
using PulseFlow.Application.Queries;
using PulseFlow.Domain;

namespace PulseFlow.Tests;

/// <summary>Shared recorder injected into test handlers and behaviors.</summary>
public sealed class Probe
{
    private int _running;
    private int _maxConcurrency;

    public ConcurrentQueue<string> Calls { get; } = new();
    public CancellationToken LastToken { get; set; }
    public int MaxConcurrency => _maxConcurrency;

    public void Enter()
    {
        var running = Interlocked.Increment(ref _running);
        int snapshot;
        do
        {
            snapshot = _maxConcurrency;
            if (running <= snapshot) break;
        } while (Interlocked.CompareExchange(ref _maxConcurrency, running, snapshot) != snapshot);
    }

    public void Exit() => Interlocked.Decrement(ref _running);
}

// ---------- Commands / queries / streams (discovered by scanning) ----------

public sealed record CreateThing(string Name) : ICommand;

public sealed class CreateThingHandler(Probe probe) : ICommandHandler<CreateThing>
{
    public ValueTask<Result> Handle(CreateThing command, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"create:{command.Name}");
        return ValueTask.FromResult(Result.Ok());
    }
}

public sealed record CreateOrder(string Name) : ICommand<Guid>;

public sealed class CreateOrderHandler(Probe probe) : ICommandHandler<CreateOrder, Guid>
{
    public static readonly Guid CreatedId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    public async ValueTask<Result<Guid>> Handle(CreateOrder command, CancellationToken cancellationToken)
    {
        await Task.Yield();
        probe.Calls.Enqueue("handler:CreateOrder");

        if (command.Name == "duplicate")
            return Error.Conflict("Order.Duplicate", "The order already exists."); // implicit Error -> Result<Guid>

        return CreatedId; // implicit Guid -> Result<Guid>
    }
}

public sealed record GetThing(int Id) : IQuery<string>;

public sealed class GetThingHandler(Probe probe) : IQueryHandler<GetThing, string>
{
    public ValueTask<Result<string>> Handle(GetThing query, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue("handler:GetThing");
        return query.Id == 0
            ? ValueTask.FromResult<Result<string>>(Error.NotFound("Thing.NotFound", "Thing 0 does not exist."))
            : ValueTask.FromResult<Result<string>>($"thing-{query.Id}");
    }
}

public sealed record CountTo(int N) : IStreamQuery<int>;

public sealed class CountToHandler : IStreamQueryHandler<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(CountTo query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= query.N; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

// Open generic handler: must be skipped by scanning instead of breaking the container.
public sealed record GenericCommand<T>(T Payload) : ICommand;

public sealed class GenericCommandHandler<T> : ICommandHandler<GenericCommand<T>>
{
    public ValueTask<Result> Handle(GenericCommand<T> command, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result.Ok());
}

// ---------- Behaviors (registered manually) ----------

public sealed class GeneralBehavior<TRequest, TResponse>(Probe probe) : IPipelineBehavior<TRequest, TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"general:{typeof(TRequest).Name}");
        return next();
    }
}

public sealed class CommandOnlyBehavior<TCommand, TResponse>(Probe probe) : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : IBaseCommand
{
    public ValueTask<TResponse> Handle(TCommand request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"command:{typeof(TCommand).Name}");
        return next();
    }
}

public sealed class QueryOnlyBehavior<TQuery, TResponse>(Probe probe) : IQueryPipelineBehavior<TQuery, TResponse>
    where TQuery : IBaseQuery
{
    public ValueTask<TResponse> Handle(TQuery request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"query:{typeof(TQuery).Name}");
        return next();
    }
}

// Uses the Result constraint to inspect the outcome without reflection.
public sealed class OutcomeBehavior<TRequest, TResponse>(Probe probe) : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result
{
    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();
        probe.Calls.Enqueue(response.IsSuccess ? "outcome:ok" : $"outcome:{response.Error.Type}");
        return response;
    }
}

// ---------- Processors and stream behaviors (2.1) ----------

// Open generic: registered with AddRequestPreProcessor / AddRequestPostProcessor.
public sealed class RecordingPreProcessor<TRequest>(Probe probe) : IRequestPreProcessor<TRequest>
{
    public ValueTask Process(TRequest request, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"pre:{typeof(TRequest).Name}");
        return ValueTask.CompletedTask;
    }
}

public sealed class RecordingPostProcessor<TRequest, TResponse>(Probe probe) : IRequestPostProcessor<TRequest, TResponse>
    where TResponse : Result
{
    public ValueTask Process(TRequest request, TResponse response, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"post:{typeof(TRequest).Name}:{(response.IsSuccess ? "ok" : response.Error.Type.ToString())}");
        return ValueTask.CompletedTask;
    }
}

// Closed: discovered by AddMediator scanning.
public sealed class GetThingPreProcessor(Probe probe) : IRequestPreProcessor<GetThing>
{
    public ValueTask Process(GetThing request, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"scanned-pre:{request.Id}");
        return ValueTask.CompletedTask;
    }
}

public sealed class TakeTwoStreamBehavior<TRequest, TResponse>(Probe probe) : IStreamPipelineBehavior<TRequest, TResponse>
{
    public async IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"stream:{typeof(TRequest).Name}");
        var count = 0;
        await foreach (var item in next().WithCancellation(cancellationToken))
        {
            if (count++ == 2)
                yield break;
            yield return item;
        }
    }
}

// ---------- Notifications discovered by scanning ----------

public sealed record ThingCreated(string Name) : INotification;

public sealed class ThingCreatedAuditHandler(Probe probe) : INotificationHandler<ThingCreated>
{
    public ValueTask Handle(ThingCreated notification, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue("audit");
        return ValueTask.CompletedTask;
    }
}

public sealed class ThingCreatedEmailHandler(Probe probe) : INotificationHandler<ThingCreated>
{
    public ValueTask Handle(ThingCreated notification, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue("email");
        return ValueTask.CompletedTask;
    }
}

// ---------- Notifications for publish-strategy tests (registered manually, in a controlled order) ----------

public sealed record Ping : INotification;

public sealed class FirstRecordingHandler(Probe probe) : INotificationHandler<Ping>
{
    public ValueTask Handle(Ping notification, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue("first");
        return ValueTask.CompletedTask;
    }
}

public sealed class SecondRecordingHandler(Probe probe) : INotificationHandler<Ping>
{
    public ValueTask Handle(Ping notification, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue("second");
        return ValueTask.CompletedTask;
    }
}

public sealed class ThrowingAsyncHandler : INotificationHandler<Ping>
{
    public async ValueTask Handle(Ping notification, CancellationToken cancellationToken)
    {
        await Task.Yield();
        throw new InvalidOperationException("async failure");
    }
}

public sealed class ThrowingSyncHandler : INotificationHandler<Ping>
{
    // Not async on purpose: throws before returning a ValueTask.
    public ValueTask Handle(Ping notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("sync failure");
}

public sealed class ConcurrencyTrackingHandler(Probe probe) : INotificationHandler<Ping>
{
    public async ValueTask Handle(Ping notification, CancellationToken cancellationToken)
    {
        probe.Enter();
        try
        {
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            probe.Exit();
        }
    }
}

// ---------- Domain ----------

public sealed record OrderPlaced(Guid OrderId) : DomainEvent;

// internal + explicit implementation: the previous dynamic-based dispatcher failed with both.
internal sealed class OrderPlacedHandler(Probe probe) : IDomainEventHandler<OrderPlaced>
{
    ValueTask IDomainEventHandler<OrderPlaced>.Handle(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue($"order:{domainEvent.OrderId}");
        probe.LastToken = cancellationToken;
        return ValueTask.CompletedTask;
    }
}

public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id) : base(id) { }

    public void Place() => AddDomainEvent(new OrderPlaced(Id));
}

public sealed class Email(string value) : ValueObject
{
    public string Value { get; } = value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}

public sealed class Username(string value) : ValueObject
{
    public string Value { get; } = value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
