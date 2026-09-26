namespace PulseFlow.Application;

/// <summary>
/// Lets generic code (pipeline behaviors) build a failed <see cref="Result"/> or <see cref="Result{T}"/>
/// without reflection: constrain with <c>where TResponse : IFailureFactory&lt;TResponse&gt;</c> and call
/// <c>TResponse.CreateFailure(error)</c>.
/// </summary>
public interface IFailureFactory<TSelf>
    where TSelf : IFailureFactory<TSelf>
{
    static abstract TSelf CreateFailure(Error error);
}
