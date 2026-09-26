using System.Text.Json.Serialization;

namespace PulseFlow.Application;

/// <summary>
/// Category of an <see cref="Error"/>. Lets callers react to a failure (for example map it to an HTTP status)
/// without parsing messages.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorType>))]
public enum ErrorType
{
    /// <summary>Generic failure (HTTP 500 / 400 depending on the host).</summary>
    Failure,

    /// <summary>The input is invalid (HTTP 400). See <see cref="Error.ValidationErrors"/>.</summary>
    Validation,

    /// <summary>The requested resource does not exist (HTTP 404).</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state (HTTP 409).</summary>
    Conflict,

    /// <summary>The caller is not authenticated (HTTP 401).</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not allowed (HTTP 403).</summary>
    Forbidden
}
