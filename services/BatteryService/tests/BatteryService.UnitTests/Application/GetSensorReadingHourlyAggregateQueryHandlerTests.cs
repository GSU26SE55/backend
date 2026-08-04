using BatteryService.Application.CQRS.Handler.SensorReading;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-06 (#650, PA-4) — handler đọc continuous aggregate qua reader (mock).
/// Query raw SQL trên TimescaleDB thật là integration; ở đây test wiring + validation.
/// </summary>
public class GetSensorReadingHourlyAggregateQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsReaderData()
    {
        var assetId = Guid.NewGuid();
        var rows = new List<SensorReadingAggregateDto>
        {
            new() { Time = new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc), MaxChargeCurrent = 2.0m, MaxDischargeCurrent = 4.0m }
        };
        var reader = new Mock<ISensorReadingAggregateViewReader>();
        reader.Setup(r => r.ReadHourlyAsync(assetId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var handler = new GetSensorReadingHourlyAggregateQueryHandler(reader.Object);
        var res = await handler.Handle(new GetSensorReadingHourlyAggregateQuery { BatteryAssetId = assetId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(200);
        res.Data.Should().HaveCount(1);
        res.Data![0].MaxChargeCurrent.Should().Be(2.0m);
    }

    [Fact]
    public async Task Validate_EmptyAssetId_Returns400_FieldInListErrors()
    {
        var res = await new GetSensorReadingHourlyAggregateQuery { BatteryAssetId = Guid.Empty }.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(400);
        res.ListErrors.Should().ContainSingle(e => e.Field == "BatteryAssetId" && !string.IsNullOrEmpty(e.Detail));
    }

    [Fact]
    public async Task Validate_FromAfterTo_Returns422_FieldInListErrors()
    {
        var res = await new GetSensorReadingHourlyAggregateQuery
        {
            BatteryAssetId = Guid.NewGuid(),
            From = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc)
        }.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(422);
        res.ListErrors.Should().ContainSingle(e => e.Field == "To" && !string.IsNullOrEmpty(e.Detail));
    }

    [Fact]
    public async Task Handle_Success_ListErrorsEmpty_SerializesNull()
    {
        var reader = new Mock<ISensorReadingAggregateViewReader>();
        reader.Setup(r => r.ReadHourlyAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SensorReadingAggregateDto>());
        var handler = new GetSensorReadingHourlyAggregateQueryHandler(reader.Object);

        var res = await handler.Handle(new GetSensorReadingHourlyAggregateQuery { BatteryAssetId = Guid.NewGuid() }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.ListErrors.Should().BeEmpty();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(res, opts));
        doc.RootElement.GetProperty("listErrors").GetRawText().Should().Be("null");
    }
}
