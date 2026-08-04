using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TicketService.Infrastructure.Persistence.Converters;

public class JsonDocumentValueConverter : ValueConverter<JsonDocument, string>
{
    private static readonly JsonDocumentOptions EmptyOptions = new();

    public JsonDocumentValueConverter() : base(
        v => v.RootElement.GetRawText(),
        v => JsonDocument.Parse(v, EmptyOptions))
    {
    }
}
