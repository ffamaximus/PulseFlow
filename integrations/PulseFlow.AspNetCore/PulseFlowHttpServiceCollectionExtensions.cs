using Microsoft.Extensions.DependencyInjection;

namespace PulseFlow.AspNetCore;

public static class PulseFlowHttpServiceCollectionExtensions
{
    /// <summary>
    /// Customizes how errors become HTTP responses (status per <see cref="PulseFlow.Application.ErrorType"/>, error code
    /// extensions). Optional: without it the defaults of <see cref="PulseFlowHttpOptions"/> are used.
    /// </summary>
    public static IServiceCollection AddPulseFlowHttp(this IServiceCollection services, Action<PulseFlowHttpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<PulseFlowHttpOptions>();
        if (configure is not null)
            builder.Configure(configure);

        return services;
    }
}
