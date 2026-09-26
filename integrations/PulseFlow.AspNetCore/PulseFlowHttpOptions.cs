using PulseFlow.Application;

namespace PulseFlow.AspNetCore;

/// <summary>
/// Controls how an <see cref="Error"/> is turned into an HTTP response. Configure it with
/// <c>services.AddPulseFlowHttp(options =&gt; ...)</c>; without configuration the defaults below are used.
/// </summary>
public sealed class PulseFlowHttpOptions
{
    internal static readonly PulseFlowHttpOptions Default = new();

    /// <summary>
    /// HTTP status code per <see cref="ErrorType"/>. Defaults: Failure 400, Validation 400, NotFound 404, Conflict 409,
    /// Unauthorized 401, Forbidden 403. Types missing from the map produce 500.
    /// </summary>
    public IDictionary<ErrorType, int> StatusCodeMap { get; } = new Dictionary<ErrorType, int>
    {
        [ErrorType.Failure] = 400,
        [ErrorType.Validation] = 400,
        [ErrorType.NotFound] = 404,
        [ErrorType.Conflict] = 409,
        [ErrorType.Unauthorized] = 401,
        [ErrorType.Forbidden] = 403
    };

    /// <summary>
    /// Adds <c>errorCode</c> (<see cref="Error.Code"/>) and <c>errorType</c> to the ProblemDetails extensions,
    /// so clients can branch on a stable code instead of parsing messages. Default: <c>true</c>.
    /// </summary>
    public bool IncludeErrorCode { get; set; } = true;

    internal int GetStatusCode(ErrorType type) => StatusCodeMap.TryGetValue(type, out var status) ? status : 500;
}
