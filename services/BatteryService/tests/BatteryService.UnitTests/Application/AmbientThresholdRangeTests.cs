using BatteryService.Application.CQRS.Command.Ambient;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Khoảng hợp lệ và luật combo trước đây chỉ tồn tại ở FE. Gọi thẳng API là ghi được
/// "500" vào một field tính bằng %, tạo ngưỡng không bao giờ trip — hỏng âm thầm, không
/// có lỗi nào để lần ra.
/// </summary>
public class AmbientThresholdRangeTests
{
    private static UpsertAmbientThresholdConfigCommand Valid() => new()
    {
        SiteId = Guid.NewGuid(),
        HighAmbientTempWarning = 35m,
        HighAmbientTempCritical = 40m,
        HighHumidityWarning = 70m,
        HighHumidityCritical = 85m,
        Enabled = true
    };

    [Fact]
    public async Task Valid_Passes()
        => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData(500)]
    [InlineData(101)]
    [InlineData(-1)]
    public async Task HumidityOutOfRange_Fails(int value)
    {
        var c = Valid();
        c.HighHumidityWarning = value;
        c.HighHumidityCritical = value;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.HighHumidityWarning));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task HumidityAtBounds_Passes(int value)
    {
        var c = Valid();
        c.HighHumidityWarning = value;
        c.HighHumidityCritical = value;

        (await c.ValidateAsync()).ListErrors
            .Should().NotContain(e => e.Field.StartsWith("HighHumidity"));
    }

    [Theory]
    [InlineData(-51)]
    [InlineData(151)]
    public async Task TemperatureOutOfRange_Fails(int value)
    {
        var c = Valid();
        c.HighAmbientTempWarning = value;
        c.HighAmbientTempCritical = value;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.HighAmbientTempWarning));
    }

    [Fact]
    public async Task ComboThresholdOutOfRange_Fails()
    {
        var c = Valid();
        c.ComboTempThreshold = 40m;
        c.ComboHumidityThreshold = 500m;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.ComboHumidityThreshold));
    }

    /// <summary>Chỉ set một nửa combo thì rule không bao giờ chạy — cấu hình chết.</summary>
    [Fact]
    public async Task ComboTempWithoutHumidity_Fails()
    {
        var c = Valid();
        c.ComboTempThreshold = 40m;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.ComboHumidityThreshold));
    }

    [Fact]
    public async Task ComboHumidityWithoutTemp_Fails()
    {
        var c = Valid();
        c.ComboHumidityThreshold = 80m;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.ComboTempThreshold));
    }

    [Fact]
    public async Task ComboBothSet_Passes()
    {
        var c = Valid();
        c.ComboTempThreshold = 40m;
        c.ComboHumidityThreshold = 80m;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ComboNeitherSet_Passes()
        => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    /// <summary>Luật cũ (critical >= warning) không được vỡ khi thêm range check.</summary>
    [Fact]
    public async Task CriticalLowerThanWarning_StillFails()
    {
        var c = Valid();
        c.HighAmbientTempWarning = 40m;
        c.HighAmbientTempCritical = 35m;

        (await c.ValidateAsync()).ListErrors
            .Should().Contain(e => e.Field == nameof(c.HighAmbientTempCritical));
    }
}
