using AuditAggregatorService.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuditAggregatorService.IntegrationTests.Audit;

/// <summary>
/// GH-729 — lịch chạy retention.
///
/// <para><b>Lỗi cũ:</b> <c>PeriodicTimer(6h)</c> neo theo lúc process khởi động, rồi mới kiểm
/// "có trong cửa sổ 03:00–04:59 không". Tick luôn rơi vào 4 mốc cố định cách nhau 6 giờ tính
/// từ lúc start, nên chỉ trúng cửa sổ khi <c>(giờ start mod 6h) ∈ [3h, 5h)</c> —
/// <b>67% mốc khởi động thì retention KHÔNG BAO GIỜ chạy</b>.</para>
///
/// <para>Các test dưới đây quét mọi mốc khởi động trong ngày; trên code cũ chúng ĐỎ.</para>
/// </summary>
public class AuditRetentionScheduleGh729Tests
{
    /// <summary>Chỉ để đọc được lịch (thành viên protected) từ test.</summary>
    private sealed class ProbeService() : AuditRetentionBackgroundService(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<AuditRetentionBackgroundService>.Instance)
    {
        public TimeSpan Delay(DateTime utcNow) => DelayUntilNextRun(utcNow);
        public bool InWindow(DateTime utcNow) => IsWithinMaintenanceWindow(utcNow);
        public int Hour => ScheduledHourUtc;
    }

    public static TheoryData<int, int> EveryStartMinuteOfDay()
    {
        var data = new TheoryData<int, int>();
        for (var h = 0; h < 24; h++)
            for (var m = 0; m < 60; m += 10)
                data.Add(h, m);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStartMinuteOfDay))]
    public void FromAnyStartTime_NextRun_LandsInsideMaintenanceWindow(int hour, int minute)
    {
        // Đây chính là test mà thiết kế cũ trượt ở 67% mốc.
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 4, hour, minute, 0, DateTimeKind.Utc);

        var next = start + sut.Delay(start);

        sut.InWindow(next).Should().BeTrue(
            $"khởi động lúc {hour:00}:{minute:00} UTC vẫn phải có lần chạy rơi vào cửa sổ bảo trì");
        next.Hour.Should().Be(sut.Hour);
        next.Minute.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryStartMinuteOfDay))]
    public void FromAnyStartTime_NextRun_IsWithin24Hours(int hour, int minute)
    {
        // Không được để lịch trôi ra xa hơn một ngày: retention 6 tháng mà chạy thưa hơn
        // 1 ngày/lần thì DB vẫn phình.
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 4, hour, minute, 0, DateTimeKind.Utc);

        var delay = sut.Delay(start);

        delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(24));
    }

    [Fact]
    public void BeforeScheduledHour_RunsSameDay()
    {
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 4, 0, 30, 0, DateTimeKind.Utc);

        (start + sut.Delay(start)).Should().Be(new DateTime(2026, 8, 4, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void AfterScheduledHour_RunsNextDay()
    {
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 4, 5, 1, 0, DateTimeKind.Utc);

        (start + sut.Delay(start)).Should().Be(new DateTime(2026, 8, 5, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ExactlyAtScheduledHour_RunsImmediately_NotTomorrow()
    {
        // Khởi động đúng 03:00:00 mà đẩy sang ngày mai là mất trọn một ngày retention.
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 4, 3, 0, 0, DateTimeKind.Utc);

        sut.Delay(start).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AcrossMonthBoundary_RollsToNextDayCorrectly()
    {
        var sut = new ProbeService();
        var start = new DateTime(2026, 8, 31, 23, 59, 0, DateTimeKind.Utc);

        (start + sut.Delay(start)).Should().Be(new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ConsecutiveRuns_AreExactly24HoursApart()
    {
        // Chạy xong lúc 03:00:0x thì lần kế phải là 03:00 hôm sau, không trôi dần.
        var sut = new ProbeService();
        var justAfterRun = new DateTime(2026, 8, 4, 3, 0, 2, DateTimeKind.Utc);

        (justAfterRun + sut.Delay(justAfterRun))
            .Should().Be(new DateTime(2026, 8, 5, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void OldSixHourTimerDesign_WouldMissWindow_ForMostStartTimes()
    {
        // Ghim lại VÌ SAO phải đổi: mô phỏng đúng thiết kế cũ (tick mỗi 6h kể từ lúc start)
        // và đếm số mốc khởi động không bao giờ trúng cửa sổ 03:00–04:59.
        var sut = new ProbeService();
        var missed = 0;
        var total = 0;

        for (var h = 0; h < 24; h++)
        {
            for (var m = 0; m < 60; m += 10)
            {
                total++;
                var start = new DateTime(2026, 8, 4, h, m, 0, DateTimeKind.Utc);
                var hits = Enumerable.Range(1, 4).Any(k => sut.InWindow(start.AddHours(6 * k)));
                if (!hits)
                    missed++;
            }
        }

        missed.Should().BeGreaterThan(total / 2,
            "thiết kế cũ trượt ở đa số mốc khởi động — đó là lý do GH-729 tồn tại");
    }
}
