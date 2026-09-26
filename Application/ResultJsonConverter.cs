using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseFlow.Application;

/// <summary>
/// System.Text.Json support for <see cref="Result"/> and <see cref="Result{T}"/>.
/// Success: <c>{ "isSuccess": true, "value": ... }</c>. Failure: <c>{ "isSuccess": false, "error": { ... } }</c>.
/// Property names follow <see cref="JsonSerializerOptions.PropertyNamingPolicy"/> and are read case-insensitively.
/// </summary>
internal sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
           || (typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(Result)
            ? new ResultJsonConverter()
            : (JsonConverter)Activator.CreateInstance(
                typeof(ResultJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

internal static class ResultJson
{
    public const string IsSuccessName = "IsSuccess";
    public const string ErrorName = "Error";
    public const string ValueName = "Value";

    public static string Name(JsonSerializerOptions options, string name)
        => options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    public static bool Is(string? propertyName, string name)
        => string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase);

    public static void WriteError(Utf8JsonWriter writer, Error error, JsonSerializerOptions options)
    {
        writer.WritePropertyName(Name(options, ErrorName));
        JsonSerializer.Serialize(writer, error, options);
    }

    public static Error ReadError(ref Utf8JsonReader reader, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<Error>(ref reader, options)
           ?? throw new JsonException("A failed result must contain an error.");
}

internal sealed class ResultJsonConverter : JsonConverter<Result>
{
    public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a JSON object for Result.");

        bool? isSuccess = null;
        Error? error = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            if (ResultJson.Is(property, ResultJson.IsSuccessName))
                isSuccess = reader.GetBoolean();
            else if (ResultJson.Is(property, ResultJson.ErrorName) && reader.TokenType != JsonTokenType.Null)
                error = ResultJson.ReadError(ref reader, options);
            else
                reader.Skip();
        }

        return (isSuccess ?? error is null)
            ? Result.Ok()
            : Result.Fail(error ?? throw new JsonException("A failed result must contain an error."));
    }

    public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(ResultJson.Name(options, ResultJson.IsSuccessName), value.IsSuccess);
        if (value.IsFailure)
            ResultJson.WriteError(writer, value.Error, options);
        writer.WriteEndObject();
    }
}

internal sealed class ResultJsonConverter<T> : JsonConverter<Result<T>>
{
    public override Result<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected a JSON object for Result<T>.");

        bool? isSuccess = null;
        Error? error = null;
        T? value = default;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            if (ResultJson.Is(property, ResultJson.IsSuccessName))
                isSuccess = reader.GetBoolean();
            else if (ResultJson.Is(property, ResultJson.ErrorName) && reader.TokenType != JsonTokenType.Null)
                error = ResultJson.ReadError(ref reader, options);
            else if (ResultJson.Is(property, ResultJson.ValueName))
                value = JsonSerializer.Deserialize<T>(ref reader, options);
            else
                reader.Skip();
        }

        return (isSuccess ?? error is null)
            ? Result<T>.Ok(value!)
            : Result<T>.Fail(error ?? throw new JsonException("A failed result must contain an error."));
    }

    public override void Write(Utf8JsonWriter writer, Result<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(ResultJson.Name(options, ResultJson.IsSuccessName), value.IsSuccess);
        if (value.IsSuccess)
        {
            writer.WritePropertyName(ResultJson.Name(options, ResultJson.ValueName));
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            ResultJson.WriteError(writer, value.Error, options);
        }
        writer.WriteEndObject();
    }
}
