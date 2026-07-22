using System.Reflection;
using BatteryService.Application.Common.Models;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.BackgroundServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>BE-AI — job nền: no-op khi disabled + convert readings đúng (cạm bẫy time/decimal).</summary>
public class SohPredictionBackgroundServiceTests
{
    private static SohPredictionBackgroundService Make(AiOptions options, IServiceScopeFactory scopeFactory)
        => new(scopeFactory, Options.Create(options), NullLogger<SohPredictionBackgroundService>.Instance);

    [Fact]
    public async Task Disabled_DoesNotCreateScope_NoOp()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var sut = Make(new AiOptions { Enabled = false }, scopeFactory.Object);

        // ExecuteAsync là protected — gọi qua StartAsync (BackgroundService) rồi stop ngay.
        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await sut.StopAsync(cts.Token);

        // Enabled=false → return trước khi đụng scope factory. Strict mock => fail nếu bị gọi.
        scopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    [Fact]
    public void BuildReadings_ConvertsTimeToRelativeSeconds_AndDecimalToDouble()
    {
        // Cạm bẫy #1: time PHẢI là giây tương đối từ reading đầu (KHÔNG phải DateTime tuyệt đối).
        var t0 = new DateTime(2026, 7, 22, 10, 0, 0, DateTimeKind.Utc);
        var window = new List<SensorReading>
        {
            MakeReading(t0, 3.9m, -1.0m, 25.0m),
            MakeReading(t0.AddSeconds(13), 3.88m, -1.1m, 25.5m),
            MakeReading(t0.AddSeconds(26), 3.86m, -1.2m, 26.0m),
        };

        var rows = InvokeBuildReadings(window);

        rows.Should().HaveCount(3);
        // Row 0: time = 0 (đầu window)
        rows[0][3].Should().Be(0.0);
        // Row 1: time = 13s tương đối
        rows[1][3].Should().Be(13.0);
        rows[2][3].Should().Be(26.0);
        // decimal → double, đúng thứ tự [voltage, current, temperature, time]
        rows[0][0].Should().BeApproximately(3.9, 1e-9);
        rows[0][1].Should().BeApproximately(-1.0, 1e-9);
        rows[0][2].Should().BeApproximately(25.0, 1e-9);
    }

    private static SensorReading MakeReading(DateTime time, decimal v, decimal i, decimal temp) => new()
    {
        Time = time,
        BatteryAssetId = Guid.NewGuid(),
        Voltage = v,
        Current = i,
        Temperature = temp,
        SocPercent = 50m,
        SourceType = SensorReadingSourceTypeEnum.Bms,
    };

    private static IReadOnlyList<double[]> InvokeBuildReadings(IReadOnlyList<SensorReading> window)
    {
        var method = typeof(SohPredictionBackgroundService)
            .GetMethod("BuildReadings", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IReadOnlyList<double[]>)method.Invoke(null, new object[] { window })!;
    }
}
