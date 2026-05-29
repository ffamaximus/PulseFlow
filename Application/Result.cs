namespace PulseFlow.Application;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        switch (isSuccess)
        {
            case true when error != null:
                throw new InvalidOperationException("A successful result cannot contain an error.");
            case false when error == null:
                throw new InvalidOperationException("A failing result must contain an error.");
            default:
                IsSuccess = isSuccess;
                Error = error;
                break;
        }
    }

    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}

public class Result<T> : Result
{
    public T Value { get; }

    private Result(bool isSuccess, T value, string? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public new static Result<T> Fail(string error) => new(false, default!, error);
}