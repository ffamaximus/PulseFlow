using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Application;
using PulseFlow.Application.Mediator;
using PulseFlow.Application.Validation;
using FV = FluentValidation;

namespace PulseFlow.Tests;

public class PipelineTests
{
    private static IMediator Build(Probe probe, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddMediator(typeof(PipelineTests).Assembly);
        configure(services);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Command_with_response_returns_its_value()
    {
        var mediator = Build(new Probe(), _ => { });

        var result = await mediator.Send(new CreateOrder("book"));

        Assert.True(result.IsSuccess);
        Assert.Equal(CreateOrderHandler.CreatedId, result.Value);
    }

    [Fact]
    public async Task Command_with_response_propagates_typed_errors()
    {
        var mediator = Build(new Probe(), _ => { });

        var result = await mediator.Send(new CreateOrder("duplicate"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("Order.Duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Command_behaviors_run_only_for_commands_and_query_behaviors_only_for_queries()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s
            .AddCommandBehavior(typeof(CommandOnlyBehavior<,>))
            .AddQueryBehavior(typeof(QueryOnlyBehavior<,>)));

        await mediator.Send(new CreateThing("a"));
        await mediator.Send(new CreateOrder("book"));
        await mediator.Send(new GetThing(1));

        var calls = probe.Calls.ToArray();
        Assert.Contains("command:CreateThing", calls);
        Assert.Contains("command:CreateOrder", calls);
        Assert.Contains("query:GetThing", calls);
        Assert.DoesNotContain("query:CreateThing", calls);
        Assert.DoesNotContain("query:CreateOrder", calls);
        Assert.DoesNotContain("command:GetThing", calls);
    }

    [Fact]
    public async Task General_behaviors_wrap_specific_behaviors_which_wrap_the_handler()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s
            .AddCommandBehavior(typeof(CommandOnlyBehavior<,>)) // registered first on purpose
            .AddPipelineBehavior(typeof(GeneralBehavior<,>)));

        await mediator.Send(new CreateOrder("book"));

        Assert.Equal(
            new[] { "general:CreateOrder", "command:CreateOrder", "handler:CreateOrder" },
            probe.Calls.ToArray());
    }

    [Fact]
    public async Task Behaviors_can_inspect_the_result_without_reflection()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s.AddPipelineBehavior(typeof(OutcomeBehavior<,>)));

        await mediator.Send(new GetThing(1));
        await mediator.Send(new GetThing(0));

        Assert.Contains("outcome:ok", probe.Calls);
        Assert.Contains("outcome:NotFound", probe.Calls);
    }

    [Fact]
    public async Task Validation_behavior_short_circuits_with_structured_errors()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s.AddPipelineBehavior(typeof(ValidationBehavior<,>)));

        var result = await mediator.Send(new CreateOrder(""));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        var failure = Assert.Single(result.Error!.ValidationErrors);
        Assert.Equal("Name", failure.PropertyName);
        Assert.DoesNotContain("handler:CreateOrder", probe.Calls);
    }

    [Fact]
    public async Task FluentValidation_behavior_supports_async_rules()
    {
        var probe = new Probe();
        var mediator = Build(probe, s => s
            .AddTransient<FV.IValidator<CreateOrder>, CreateOrderFluentValidator>()
            .AddFluentValidationIntegration());

        var rejected = await mediator.Send(new CreateOrder("reserved"));
        var accepted = await mediator.Send(new CreateOrder("book"));

        Assert.Equal(ErrorType.Validation, rejected.Error!.Type);
        Assert.Equal("Name is reserved.", Assert.Single(rejected.Error!.ValidationErrors).ErrorMessage);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public void Registering_a_behavior_under_the_wrong_contract_fails_fast()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCommandBehavior(typeof(QueryOnlyBehavior<,>)));
        Assert.Throws<ArgumentException>(() => services.AddPipelineBehavior(typeof(GeneralBehavior<int, int>)));
    }
}
