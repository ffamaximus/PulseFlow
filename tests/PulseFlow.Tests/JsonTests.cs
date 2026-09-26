using System.Text.Json;
using PulseFlow.Application;
using PulseFlow.Application.Validation;

namespace PulseFlow.Tests;

public class JsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public sealed record Dto(int Id, string Name);

    [Fact]
    public void Successful_result_serializes_its_value()
    {
        Result<Dto> result = new Dto(1, "a");

        var json = JsonSerializer.Serialize(result, Web);

        Assert.Equal("""{"isSuccess":true,"value":{"id":1,"name":"a"}}""", json);
    }

    [Fact]
    public void Failed_result_serializes_without_throwing()
    {
        Result<Dto> result = Error.NotFound("Dto.NotFound", "Missing");

        var json = JsonSerializer.Serialize(result, Web);

        Assert.Equal(
            """{"isSuccess":false,"error":{"code":"Dto.NotFound","message":"Missing","type":"NotFound","validationErrors":[]}}""",
            json);
    }

    [Fact]
    public void Non_generic_result_serializes()
    {
        Assert.Equal("""{"isSuccess":true}""", JsonSerializer.Serialize(Result.Ok(), Web));
        Assert.Contains("\"type\":\"Conflict\"", JsonSerializer.Serialize(Result.Fail(Error.Conflict("c", "m")), Web));
    }

    [Fact]
    public void Result_declared_as_base_type_uses_the_converter()
    {
        Result result = Result.Fail("boom");

        var json = JsonSerializer.Serialize(result, Web);

        Assert.StartsWith("{\"isSuccess\":false,\"error\":", json);
    }

    [Fact]
    public void Results_round_trip()
    {
        Result<Dto> ok = new Dto(7, "x");
        Result<Dto> failed = Error.Validation([new ValidationError("Name", "Required")]);

        var okBack = JsonSerializer.Deserialize<Result<Dto>>(JsonSerializer.Serialize(ok, Web), Web)!;
        var failedBack = JsonSerializer.Deserialize<Result<Dto>>(JsonSerializer.Serialize(failed, Web), Web)!;
        var plainBack = JsonSerializer.Deserialize<Result>(JsonSerializer.Serialize(Result.Fail("boom"), Web), Web)!;

        Assert.Equal(new Dto(7, "x"), okBack.Value);

        Assert.True(failedBack.IsFailure);
        Assert.Equal(ErrorType.Validation, failedBack.Error!.Type);
        Assert.Equal(new ValidationError("Name", "Required"), Assert.Single(failedBack.Error.ValidationErrors));

        Assert.True(plainBack.IsFailure);
        Assert.Equal("boom", plainBack.Error!.Message);
    }

    [Fact]
    public void Property_names_follow_the_naming_policy()
    {
        var json = JsonSerializer.Serialize(Result<int>.Ok(1), new JsonSerializerOptions());

        Assert.Equal("""{"IsSuccess":true,"Value":1}""", json);
        Assert.Equal(1, JsonSerializer.Deserialize<Result<int>>(json, Web)!.Value); // reading is case-insensitive
    }
}
