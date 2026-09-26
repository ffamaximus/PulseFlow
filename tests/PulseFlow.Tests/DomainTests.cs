using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Domain;

namespace PulseFlow.Tests;

public class DomainTests
{
    [Fact]
    public async Task Dispatcher_invokes_internal_explicit_handlers_and_passes_the_token()
    {
        var probe = new Probe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddTransient<IDomainEventHandler<OrderPlaced>, OrderPlacedHandler>();
        using var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();
        await new DomainEventDispatcher(provider).DispatchAsync([new OrderPlaced(id)], cts.Token);

        Assert.Equal(new[] { $"order:{id}" }, probe.Calls.ToArray());
        Assert.Equal(cts.Token, probe.LastToken);
    }

    [Fact]
    public void Value_objects_of_different_types_are_not_equal()
    {
        ValueObject email = new Email("a");
        ValueObject username = new Username("a");

        Assert.False(email.Equals(username));
        Assert.False(email == username);
        Assert.True(email != username);
    }

    [Fact]
    public void Value_objects_of_the_same_type_compare_by_components()
    {
        var a = new Email("a");
        var b = new Email("a");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new Email("b"));
    }

    [Fact]
    public void Value_object_null_comparisons()
    {
        Email? none = null;

        Assert.True(none == null);
        Assert.False(new Email("a") == null);
        Assert.False(new Email("a").Equals(null));
    }
}
