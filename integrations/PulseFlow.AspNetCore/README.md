# PulseFlow.AspNetCore

ASP.NET Core integration for [PulseFlow](https://www.nuget.org/packages/PulseFlow/): commands and queries as minimal API
endpoints in one line, and `Result` / `Error` turned into HTTP responses and RFC 9457 ProblemDetails automatically.

```bash
dotnet add package PulseFlow.AspNetCore
```

```csharp
using PulseFlow.AspNetCore;

builder.Services.AddMediator(typeof(CreateOrder).Assembly);
builder.Services.AddProblemDetails();          // optional, recommended
builder.Services.AddPulseFlowHttp();           // optional: customize the status mapping

app.MapCommand<CreateOrder, Guid>("/orders", id => $"/orders/{id}");   // POST, body   -> 201 Created
app.MapCommand<CancelOrder>("/orders/{id}", HttpMethods.Delete);       // DELETE, route -> 204 No Content
app.MapQuery<GetOrder, OrderDto>("/orders/{id}");                      // GET, route/query -> 200 OK
```

Hand-written endpoints use the same mapping:

```csharp
app.MapPut("/orders/{id}", async (Guid id, UpdateOrderBody body, IMediator mediator, CancellationToken ct) =>
    (await mediator.Send(new UpdateOrder(id, body.Name), ct)).ToHttpResult());
```

| `ErrorType` | Status (default) | Body |
|---|---|---|
| `Validation` | 400 | `HttpValidationProblemDetails` with `errors` per property |
| `Failure` | 400 | ProblemDetails |
| `NotFound` | 404 | ProblemDetails |
| `Conflict` | 409 | ProblemDetails |
| `Unauthorized` | 401 | ProblemDetails |
| `Forbidden` | 403 | ProblemDetails |

Every ProblemDetails includes `errorCode` (`Error.Code`) and `errorType` extensions. Change the mapping with
`AddPulseFlowHttp(o => o.StatusCodeMap[ErrorType.Validation] = 422)` or remove the extensions with
`o.IncludeErrorCode = false`. When `AddProblemDetails()` is registered its customizations are applied as well.
