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
}
