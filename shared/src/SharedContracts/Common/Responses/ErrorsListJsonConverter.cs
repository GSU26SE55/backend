using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedContracts.Common.Responses;

internal sealed class ErrorsListJsonConverter : JsonConverter<List<Errors>>
{
    public override List<Errors> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new List<Errors>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected start of array or null for ListErrors.");

        var errors = new List<Errors>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return errors;

            var error = JsonSerializer.Deserialize<Errors>(ref reader, options);
            if (error is not null)
                errors.Add(error);
        }

        throw new JsonException("Unexpected end of JSON while reading ListErrors.");
    }

    public override void Write(Utf8JsonWriter writer, List<Errors> value, JsonSerializerOptions options)
    {
        if (value.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var error in value)
            JsonSerializer.Serialize(writer, error, options);
        writer.WriteEndArray();
    }
}
