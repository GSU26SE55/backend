using System.Text.Json;
using BatteryService.Application.DTOs.Realtime;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Parity contract (§34.10.5): event SSE <c>summary</c> (scope assets/customer/site/type/all/...)
/// mang ĐẦY ĐỦ field y hệt event <c>reading</c> — mỗi item là <see cref="LiveReadingDto"/> trọn bộ.
/// Chống hồi quy "thiếu thông số pin" ở các scope ngoài single-asset (current, chargingState,
/// bmsErrorCode, cycleCount, sourceType... từng bị rút gọn trong bản BatterySummaryItemDto cũ).
/// </summary>
public class RealtimeSummaryContractTests
{
    // Khớp RedisTelemetryPublisher.JsonOptions (camelCase + bỏ null khi ghi).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SummaryItems_AreFullLiveReadingDto_NotReducedShape()
    {
        // Type-level guarantee: Items là List<LiveReadingDto> → thêm field vào reading thì summary
        // có ngay, không phải sửa 2 nơi (chống thiếu sót trong tương lai).
        typeof(BatterySummaryDto).GetProperty(nameof(BatterySummaryDto.Items))!
            .PropertyType.Should().Be(typeof(List<LiveReadingDto>));
    }

    [Fact]
    public void SummaryJson_ContainsAllTelemetryFields()
    {
        var summary = new BatterySummaryDto
        {
            ScopeType = "site",
            Items = new List<LiveReadingDto>
            {
                new()
                {
                    BatteryAssetId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), SiteId = Guid.NewGuid(),
                    BatteryTypeId = Guid.NewGuid(), Time = DateTime.UtcNow,
                    Voltage = 12.6m, Current = -5.2m, Temperature = 35.4m, SocPercent = 78.5m, SohPercent = 94.2m,
                    CycleCount = 120, ChargingState = 3, InternalResistanceMilliohm = 8.1m, CellVoltageDeltaMv = 12m,
                    BmsErrorCode = "OVT-PROTECT", SourceDeviceId = "ESP32-SIM-001", SourceType = 1,
                    SensorSourceCode = "primary"
                }
            }
        };

        var json = JsonSerializer.Serialize(summary, JsonOptions);

        // Mọi field mà bản summary RÚT GỌN cũ thiếu — nay phải có mặt (parity với reading).
        string[] requiredFields =
        {
            "current", "sohPercent", "cycleCount", "chargingState",
            "internalResistanceMilliohm", "cellVoltageDeltaMv", "bmsErrorCode",
            "sourceDeviceId", "sourceType", "sensorSourceCode",
            "customerId", "siteId", "batteryTypeId",
            // các field vốn đã có
            "batteryAssetId", "time", "voltage", "temperature", "socPercent"
        };
        foreach (var field in requiredFields)
            json.Should().Contain($"\"{field}\"", $"summary item phải có field '{field}' (parity với event reading)");
    }
}
