using BatteryService.Application.Common.Models;
using BatteryService.Domain.Enums;
using FluentAssertions;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>BE-AI — map chuỗi classification của AI → enum BE (Normal=1/Degrading=2/Failed=3).</summary>
public class AiPredictionResultTests
{
    [Theory]
    [InlineData("Normal", AnomalyClassificationEnum.Normal)]
    [InlineData("Degrading", AnomalyClassificationEnum.Degrading)]
    [InlineData("Failed", AnomalyClassificationEnum.Failed)]
    [InlineData("  Degrading  ", AnomalyClassificationEnum.Degrading)] // trim
    public void ParseClassification_KnownValues_MapsCorrectly(string raw, AnomalyClassificationEnum expected)
    {
        AiPredictionResult.ParseClassification(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Weird")]
    public void ParseClassification_UnknownOrNull_DefaultsToNormal(string? raw)
    {
        // Fallback an toàn — không tự tạo Alert từ giá trị lạ.
        AiPredictionResult.ParseClassification(raw).Should().Be(AnomalyClassificationEnum.Normal);
    }

    // ── GH-805: ResolveSeverity ────────────────────────────────────────────

    [Theory]
    // Không tín hiệu nào → không raise alert (hành vi trước GH-805, phải giữ nguyên).
    [InlineData(AnomalyClassificationEnum.Normal, "None")]
    [InlineData(AnomalyClassificationEnum.Normal, "P3")]
    [InlineData(AnomalyClassificationEnum.Normal, "")]
    [InlineData(AnomalyClassificationEnum.Normal, null)]
    // Chuỗi priority lạ không được coi là tín hiệu raise.
    [InlineData(AnomalyClassificationEnum.Normal, "HIGH")]
    public void ResolveSeverity_NoSignal_ReturnsNull(
        AnomalyClassificationEnum classification, string? priority)
    {
        AiPredictionResult.ResolveSeverity(classification, priority).Should().BeNull();
    }

    [Theory]
    // Repro của issue #805: AI trả Normal nhưng risk.priority = P1 (VD nhiệt 50°C, SOH vẫn 95%).
    [InlineData(AnomalyClassificationEnum.Normal, "P1", AlertSeverityEnum.Critical)]
    [InlineData(AnomalyClassificationEnum.Normal, "P2", AlertSeverityEnum.Warning)]
    // Classification vẫn hoạt động độc lập như trước.
    [InlineData(AnomalyClassificationEnum.Degrading, "None", AlertSeverityEnum.Warning)]
    [InlineData(AnomalyClassificationEnum.Degrading, "P3", AlertSeverityEnum.Warning)]
    [InlineData(AnomalyClassificationEnum.Failed, "None", AlertSeverityEnum.Critical)]
    // Lấy mức CAO HƠN giữa hai nguồn.
    [InlineData(AnomalyClassificationEnum.Degrading, "P1", AlertSeverityEnum.Critical)]
    // Failed KHÔNG bị P2/P3 hạ xuống Warning — pin hỏng vẫn phải có ticket.
    [InlineData(AnomalyClassificationEnum.Failed, "P2", AlertSeverityEnum.Critical)]
    [InlineData(AnomalyClassificationEnum.Failed, "P3", AlertSeverityEnum.Critical)]
    // Priority không phân biệt hoa thường / khoảng trắng thừa.
    [InlineData(AnomalyClassificationEnum.Normal, "p1", AlertSeverityEnum.Critical)]
    [InlineData(AnomalyClassificationEnum.Normal, "  P2  ", AlertSeverityEnum.Warning)]
    public void ResolveSeverity_TakesHigherOfClassificationAndRisk(
        AnomalyClassificationEnum classification, string? priority, AlertSeverityEnum expected)
    {
        AiPredictionResult.ResolveSeverity(classification, priority).Should().Be(expected);
    }

    // ── GH-805: MapWarningToAnomalyType ────────────────────────────────────

    [Theory]
    [InlineData("TEMP_CRITICAL", AnomalyTypeEnum.Overheat)]
    [InlineData("TEMP_HIGH", AnomalyTypeEnum.Overheat)]
    [InlineData("TEMP_LOW", AnomalyTypeEnum.Undertemp)]
    [InlineData("VOLTAGE_HIGH", AnomalyTypeEnum.Overvoltage)]
    [InlineData("VOLTAGE_LOW", AnomalyTypeEnum.Undervoltage)]
    [InlineData("SOH_LOW", AnomalyTypeEnum.SohDegradation)]
    // Code lạ → fallback, không đoán bừa loại sự cố.
    [InlineData("SOMETHING_NEW", AnomalyTypeEnum.SohDegradation)]
    public void MapWarningToAnomalyType_MapsKnownCodes(string code, AnomalyTypeEnum expected)
    {
        var warnings = new[] { new AiWarningItem(code, "critical", "msg") };

        AiPredictionResult.MapWarningToAnomalyType(warnings).Should().Be(expected);
    }

    [Fact]
    public void MapWarningToAnomalyType_NoWarnings_FallsBackToSohDegradation()
    {
        AiPredictionResult.MapWarningToAnomalyType(null).Should().Be(AnomalyTypeEnum.SohDegradation);
        AiPredictionResult.MapWarningToAnomalyType(Array.Empty<AiWarningItem>())
            .Should().Be(AnomalyTypeEnum.SohDegradation);
    }

    [Fact]
    public void MapWarningToAnomalyType_PrefersCriticalWarningOverEarlierWarning()
    {
        // Warning thường đứng trước, critical đứng sau → phải chọn critical.
        var warnings = new[]
        {
            new AiWarningItem("VOLTAGE_LOW", "warning", "msg"),
            new AiWarningItem("TEMP_CRITICAL", "critical", "msg"),
        };

        AiPredictionResult.MapWarningToAnomalyType(warnings).Should().Be(AnomalyTypeEnum.Overheat);
    }

    [Fact]
    public void MapWarningToAnomalyType_NoCriticalWarning_UsesFirstItem()
    {
        var warnings = new[]
        {
            new AiWarningItem("TEMP_LOW", "warning", "msg"),
            new AiWarningItem("VOLTAGE_HIGH", "warning", "msg"),
        };

        AiPredictionResult.MapWarningToAnomalyType(warnings).Should().Be(AnomalyTypeEnum.Undertemp);
    }
}
