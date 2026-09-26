namespace PulseFlow.Application.Queries;

/// <summary>A query whose results are streamed as an <see cref="IAsyncEnumerable{T}"/>.</summary>
public interface IStreamQuery<TResponse> : IBaseQuery { }
