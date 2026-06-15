using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TicketService.Infrastructure.Persistence.Converters;

public class JsonValueConverter<T> : ValueConverter<T, string>
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonValueConverter() : base(
        v => JsonSerializer.Serialize(v, DefaultOptions),
        v => JsonSerializer.Deserialize<T>(v, DefaultOptions) ?? default!)
    {
    }
}
