using System.Text.Json;
using SharedContracts.Common.Responses;

namespace SharedInfrastructure.UnitTests.Contracts;

public class CommonResponseSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Serialize_EmptyListErrors_WritesNull()
    {
        var response = new CommonResponse<string>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Not found"
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("listErrors").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Serialize_FieldValidationErrors_WritesArray()
    {
        var response = new CommonResponse<string>
        {
            IsSuccess = false,
            StatusCode = 400
        };
        response.ListErrors.Add(new Errors { Field = "Email", Detail = "Invalid email format." });

        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);

        var listErrors = doc.RootElement.GetProperty("listErrors");
        listErrors.ValueKind.Should().Be(JsonValueKind.Array);
        listErrors.GetArrayLength().Should().Be(1);
        listErrors[0].GetProperty("field").GetString().Should().Be("Email");
        listErrors[0].GetProperty("detail").GetString().Should().Be("Invalid email format.");
    }
}
