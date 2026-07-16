using BatteryService.Application.Realtime;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-03 (#648) — quy ước window (label/start/keySuffix/TTL) — chốt chỉ 1h + today (Q3).
/// </summary>
public class StatsWindowsTests
{
    private static readonly DateTime T = new(2026, 7, 8, 9, 41, 5, DateTimeKind.Utc);

    [Fact]
    public void Labels_MatchSsePayloadContract()
    {
        StatsWindows.Label(StatsWindowType.Hour).Should().Be("1h");
        StatsWindows.Label(StatsWindowType.Today).Should().Be("today");
    }

    [Fact]
    public void WindowStart_Hour_FloorsToHour()
        => StatsWindows.WindowStart(StatsWindowType.Hour, T).Should().Be(new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void WindowStart_Today_FloorsToMidnightUtc()
        => StatsWindows.WindowStart(StatsWindowType.Today, T).Should().Be(new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void KeySuffix_Hour_ChangesWhenHourRollsOver()
    {
        var suffix9 = StatsWindows.KeySuffix(StatsWindowType.Hour, T);
        var suffix10 = StatsWindows.KeySuffix(StatsWindowType.Hour, T.AddHours(1));

        suffix9.Should().Be("2026070809");
        suffix10.Should().Be("2026070810");
        suffix9.Should().NotBe(suffix10, "sang giờ mới → key bucket mới");
    }

    [Fact]
    public void KeySuffix_Today_Format()
        => StatsWindows.KeySuffix(StatsWindowType.Today, T).Should().Be("20260708");

    [Fact]
    public void Ttl_HourTwoHours_TodayTwentySixHours()
    {
        StatsWindows.Ttl(StatsWindowType.Hour).Should().Be(TimeSpan.FromHours(2));
        StatsWindows.Ttl(StatsWindowType.Today).Should().Be(TimeSpan.FromHours(26));
    }

    [Fact]
    public void All_ContainsExactlyTwoWindows()
        => StatsWindows.All.Should().BeEquivalentTo(new[] { StatsWindowType.Hour, StatsWindowType.Today });
}
