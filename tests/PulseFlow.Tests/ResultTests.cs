using PulseFlow.Application;
using PulseFlow.Application.Validation;

namespace PulseFlow.Tests;

public class ResultTests
{
    [Fact]
    public void Error_factories_set_the_type()
    {
        Assert.Equal(ErrorType.Failure, Error.Failure("c", "m").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("c", "m").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("c", "m").Type);
        Assert.Equal(ErrorType.Unauthorized, Error.Unauthorized("c", "m").Type);
        Assert.Equal(ErrorType.Forbidden, Error.Forbidden("c", "m").Type);
        Assert.Equal(ErrorType.Validation, Error.Validation("c", "m").Type);
    }

    [Fact]
    public void Error_requires_a_code()
        => Assert.ThrowsAny<ArgumentException>(() => new Error(" ", "m"));

    [Fact]
    public void Validation_error_keeps_structured_failures()
    {
        var error = Error.Validation(
        [
            new ValidationError("Name", "Required"),
            new ValidationError("Name", "Too short"),
            new ValidationError("Age", "Must be positive")
        ]);

        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal(3, error.ValidationErrors.Count);

        var dictionary = error.ToValidationDictionary();
        Assert.Equal(new[] { "Required", "Too short" }, dictionary["Name"]);
        Assert.Equal(new[] { "Must be positive" }, dictionary["Age"]);
    }

    [Fact]
    public void Ok_is_cached()
        => Assert.Same(Result.Ok(), Result.Ok());

    [Fact]
    public void Fail_with_message_creates_a_failure_error()
    {
        var result = Result.Fail("boom");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error!.Type);
        Assert.Equal("boom", result.Error!.Message);
    }

    [Fact]
    public void Implicit_conversions()
    {
        Result fromError = Error.Conflict("c", "m");
        Result<int> fromValue = 42;
        Result<int> fromTypedError = Error.NotFound("c", "m");

        Assert.True(fromError.IsFailure);
        Assert.Equal(42, fromValue.Value);
        Assert.Equal(ErrorType.NotFound, fromTypedError.Error!.Type);
    }

    [Fact]
    public void Failed_result_value_throws()
    {
        var result = Result<int>.Fail("boom");

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("boom", ex.Message);
        Assert.Equal(0, result.ValueOrDefault);
    }

    [Fact]
    public void Map_and_Bind_propagate_failures()
    {
        Result<int> ok = 2;
        Result<int> failed = Error.NotFound("c", "m");

        Assert.Equal("4", ok.Map(x => x * 2).Map(x => x.ToString()).Value);
        Assert.Equal(ErrorType.NotFound, failed.Map(x => x * 2).Error!.Type);

        Assert.Equal(3, ok.Bind(x => Result<int>.Ok(x + 1)).Value);
        Assert.Equal(ErrorType.Conflict, ok.Bind(_ => (Result<int>)Error.Conflict("c", "m")).Error!.Type);
        Assert.Equal(ErrorType.NotFound, failed.Bind(x => Result<int>.Ok(x + 1)).Error!.Type);
    }

    [Fact]
    public void Match_selects_the_branch()
    {
        Result<int> ok = 2;
        Result<int> failed = Error.NotFound("c", "m");

        Assert.Equal("value:2", ok.Match(v => $"value:{v}", e => $"error:{e.Code}"));
        Assert.Equal("error:c", failed.Match(v => $"value:{v}", e => $"error:{e.Code}"));
        Assert.Equal("ok", Result.Ok().Match(() => "ok", e => e.Code));
    }
}
