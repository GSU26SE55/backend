using SmsService.Domain.Entities;

namespace SmsService.UnitTests.Domain;

public class SmsGatewayDeviceTests
{
    private static SmsGatewayDevice NewDevice(int dailyLimit = 100, int sentToday = 0, DateOnly? sentTodayDate = null) => new()
    {
        Id = Guid.NewGuid(),
        DeviceName = "Phone A",
        DeviceCode = "device-A",
        ApiKeyHash = "$2b$11$dummy",
        IsActive = true,
        DailyLimit = dailyLimit,
        SentToday = sentToday,
        SentTodayDate = sentTodayDate,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Touch_UpdatesLastSeenAtAndIp()
    {
        var device = NewDevice();
        var now = DateTime.UtcNow;

        device.Touch("10.0.0.5", now);

        device.LastSeenAt.Should().Be(now);
        device.LastSeenIp.Should().Be("10.0.0.5");
        device.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void ResetDailyCounterIfNeeded_NewDay_ResetsCount()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var device = NewDevice(dailyLimit: 100, sentToday: 50, sentTodayDate: yesterday);
        var now = DateTime.UtcNow;

        device.ResetDailyCounterIfNeeded(now);

        device.SentToday.Should().Be(0);
        device.SentTodayDate.Should().Be(DateOnly.FromDateTime(now));
    }

    [Fact]
    public void ResetDailyCounterIfNeeded_SameDay_NoChange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var device = NewDevice(dailyLimit: 100, sentToday: 50, sentTodayDate: today);

        device.ResetDailyCounterIfNeeded(DateTime.UtcNow);

        device.SentToday.Should().Be(50);
        device.SentTodayDate.Should().Be(today);
    }

    [Fact]
    public void IncrementSent_ResetsIfNewDay_AndIncrements()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var device = NewDevice(dailyLimit: 100, sentToday: 99, sentTodayDate: yesterday);
        var now = DateTime.UtcNow;

        device.IncrementSent(now);

        // Reset trước rồi increment → SentToday = 1 (không phải 100)
        device.SentToday.Should().Be(1);
        device.SentTodayDate.Should().Be(DateOnly.FromDateTime(now));
    }

    [Fact]
    public void Revoke_SetsInactive_AndRevokedAt()
    {
        var device = NewDevice();
        var now = DateTime.UtcNow;

        device.Revoke(now);

        device.IsActive.Should().BeFalse();
        device.RevokedAt.Should().Be(now);
    }
}
