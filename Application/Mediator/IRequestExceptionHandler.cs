namespace PulseFlow.Application.Mediator;

/// <summary>
/// Handles an exception thrown by the handler (or by a pre/post-processor) of <typeparamref name="TRequest"/>.
/// Handlers run in registration order; the first one that returns
/// <see cref="ExceptionHandlingResult{TResponse}.Handle(TResponse)"/> replaces the exception with that response,
/// otherwise the exception is rethrown. Typical use: turn infrastructure exceptions into typed failures, e.g.
/// <c>DbUpdateConcurrencyException</c> → <c>Error.Conflict(...)</c> (constrain <c>TResponse : IFailureFactory&lt;TResponse&gt;</c>).
/// Cancellations requested by the caller are never passed to exception handlers.
/// Closed implementations are discovered by <c>AddMediator</c>; open generic ones are registered with
/// <c>services.AddRequestExceptionHandler(typeof(MyHandler&lt;,&gt;))</c>.
/// </summary>
public interface IRequestExceptionHandler<in TRequest, TResponse>
{
    ValueTask<ExceptionHandlingResult<TResponse>> Handle(TRequest request, Exception exception, CancellationToken cancellationToken);
}

/// <summary>Outcome of an <see cref="IRequestExceptionHandler{TRequest,TResponse}"/>.</summary>
public readonly struct ExceptionHandlingResult<TResponse>
{
    private ExceptionHandlingResult(TResponse response)
    {
        Handled = true;
        Response = response;
    }

    /// <summary>True when the exception was handled and <see cref="Response"/> must be returned instead.</summary>
    public bool Handled { get; }

    public TResponse? Response { get; }

    /// <summary>Let the next exception handler try; if none handles it, the exception is rethrown.</summary>
    public static ExceptionHandlingResult<TResponse> NotHandled => default;

    /// <summary>Stop the exception and return <paramref name="response"/> to the caller.</summary>
    public static ExceptionHandlingResult<TResponse> Handle(TResponse response) => new(response);
}
