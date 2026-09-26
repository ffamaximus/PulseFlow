using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application;
using PulseFlow.Application.Mediator;
using PulseFlow.Application.Validation;
using PulseFlow.AspNetCore;

namespace PulseFlow.Tests;

public class AspNetCoreTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new Probe());
        builder.Services.AddMediator(typeof(AspNetCoreTests).Assembly);
        builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapCommand<CreateThing>("/things");
        app.MapCommand<CreateOrder, Guid>("/orders", id => $"/orders/{id}");
        app.MapCommand<CreateOrder, Guid>("/orders-ok");
        app.MapQuery<GetThing, string>("/things/{id}");
        app.MapGet("/manual/{id}", (int id) => (id == 0
            ? Result<string>.Fail(Error.Forbidden("Thing.Forbidden", "No access."))
            : Result<string>.Ok("ok")).ToHttpResult());

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>());

    // Dictionary keys may or may not be camel-cased depending on the JSON options; compare ignoring case.
    private static JsonElement Property(JsonElement element, string name)
        => element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public async Task Command_without_response_returns_204()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.PostAsJsonAsync("/things", new { name = "a" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Command_with_location_returns_201_with_value_and_location()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.PostAsJsonAsync("/orders", new { name = "book" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/orders/{CreateOrderHandler.CreatedId}", response.Headers.Location!.OriginalString);
        Assert.Equal(CreateOrderHandler.CreatedId, await response.Content.ReadFromJsonAsync<Guid>());
    }

    [Fact]
    public async Task Command_without_location_returns_200_with_value()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.PostAsJsonAsync("/orders-ok", new { name = "book" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CreateOrderHandler.CreatedId, await response.Content.ReadFromJsonAsync<Guid>());
    }

    [Fact]
    public async Task Validation_error_returns_400_validation_problem()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.PostAsJsonAsync("/orders", new { name = "" });
        var json = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Name is required.", Property(json.GetProperty("errors"), "Name")[0].GetString());
        Assert.Equal("Validation", json.GetProperty("errorType").GetString());
    }

    [Fact]
    public async Task Conflict_error_returns_409_with_error_code()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.PostAsJsonAsync("/orders", new { name = "duplicate" });
        var json = await ReadJson(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Order.Duplicate", json.GetProperty("errorCode").GetString());
        Assert.Equal("The order already exists.", json.GetProperty("detail").GetString());
        Assert.Equal(409, json.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Query_binds_route_values_and_returns_200()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.GetAsync("/things/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("thing-5", await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task Not_found_error_returns_404()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        var response = await client.GetAsync("/things/0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Thing.NotFound", (await ReadJson(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Manual_endpoints_use_the_same_mapping()
    {
        var (app, client) = await StartAsync();
        await using var host = app;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/manual/0")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/manual/1")).StatusCode);
    }

    [Fact]
    public async Task Status_mapping_and_extensions_are_configurable()
    {
        var (app, client) = await StartAsync(s => s.AddPulseFlowHttp(o =>
        {
            o.StatusCodeMap[ErrorType.Validation] = StatusCodes.Status422UnprocessableEntity;
            o.IncludeErrorCode = false;
        }));
        await using var host = app;

        var response = await client.PostAsJsonAsync("/orders", new { name = "" });
        var json = await ReadJson(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True(json.TryGetProperty("errors", out _));
        Assert.False(json.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public async Task AddProblemDetails_customizations_are_applied()
    {
        var (app, client) = await StartAsync(s => s.AddProblemDetails(o =>
            o.CustomizeProblemDetails = ctx => ctx.ProblemDetails.Extensions["traceId"] = "trace-123"));
        await using var host = app;

        var json = await ReadJson(await client.GetAsync("/things/0"));

        Assert.Equal("trace-123", json.GetProperty("traceId").GetString());
        Assert.Equal("Thing.NotFound", json.GetProperty("errorCode").GetString());
    }
}
