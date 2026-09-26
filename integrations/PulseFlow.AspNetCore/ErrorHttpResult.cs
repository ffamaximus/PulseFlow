using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PulseFlow.Application;

namespace PulseFlow.AspNetCore;

/// <summary>
/// Writes an <see cref="Error"/> as RFC 9457 ProblemDetails. The status code is resolved at execution time from
/// <see cref="PulseFlowHttpOptions"/>, and the response goes through <c>IProblemDetailsService</c> when
/// <c>AddProblemDetails()</c> is registered (so its customizations apply).
/// </summary>
internal sealed class ErrorHttpResult(Error error) : IResult
{
    public Error Error { get; } = error;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = httpContext.RequestServices.GetService<IOptions<PulseFlowHttpOptions>>()?.Value
                      ?? PulseFlowHttpOptions.Default;
        var problem = ToProblemDetails(Error, options);

        // Prefer IProblemDetailsService (AddProblemDetails) so CustomizeProblemDetails and custom writers apply.
        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            httpContext.Response.StatusCode = problem.Status!.Value;
            if (await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }))
                return;
        }

        await TypedResults.Problem(problem).ExecuteAsync(httpContext);
    }

    internal static ProblemDetails ToProblemDetails(Error error, PulseFlowHttpOptions options)
    {
        var status = options.GetStatusCode(error.Type);

        var problem = error.Type == ErrorType.Validation
            ? new HttpValidationProblemDetails(error.ToValidationDictionary())
            : new ProblemDetails();

        var title = ReasonPhrases.GetReasonPhrase(status);
        problem.Status = status;
        problem.Title = string.IsNullOrEmpty(title) ? null : title;
        problem.Detail = error.Message;

        if (options.IncludeErrorCode)
        {
            problem.Extensions["errorCode"] = error.Code;
            problem.Extensions["errorType"] = error.Type.ToString();
        }

        return problem;
    }
}
