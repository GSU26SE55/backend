using TicketService.Domain.Entities;
using TicketService.Infrastructure.BackgroundJobs;
using SharedContracts.Events;

namespace TicketService.UnitTests.Handlers.PeriodicMaintenance;

/// <summary>
/// Ba mốc nhắc của ticket bảo trì định kỳ: nhắc lần đầu ngày mở, lần hai ngày kế, ngày thứ
/// ba bàn cho Manager.
/// </summary>
/// <remarks>
/// Mốc tính theo <b>ngày địa phương</b> chứ không theo số giờ trôi qua — "08:00 sáng hôm sau"
/// là thứ khách hiểu, còn "sau 24 giờ" thì rơi vào giữa đêm với ticket mở lúc 2 giờ sáng.
/// Đó là lý do bộ test này dựng mốc thời gian quanh nửa đêm giờ Việt Nam.
/// </remarks>
public class ReminderStageTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly TimeSpan EightAm = TimeSpan.FromHours(8);

    /// <summary>Ticket mở lúc 02:00 giờ Việt Nam ngày 10/09/2026 (19:00 UTC ngày 09).</summary>
    private static Ticket Ticket(
        DateTime? reminder1 = null, DateTime? reminder2 = null,
        DateTime? escalated = null, DateTime? scheduledStart = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = "PM-0001",
            Title = "Periodic battery maintenance",
            Description = "Scheduled maintenance.",
            CreatedAt = new DateTime(2026, 9, 9, 19, 0, 0, DateTimeKind.Utc),
            PeriodicMaintenanceDueAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            PeriodicMaintenanceScheduleDeadlineAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            PeriodicMaintenanceReminder1SentAtUtc = reminder1,
            PeriodicMaintenanceReminder2SentAtUtc = reminder2,
            PeriodicMaintenanceManagerEscalatedAtUtc = escalated,
            ScheduledStartAtUtc = scheduledStart,
        };

    /// <summary>08:00 giờ Việt Nam của ngày 10 + <paramref name="dayOffset"/>, quy về UTC.</summary>
    private static DateTime EightAmLocal(int dayOffset) =>
        TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 9, 10 + dayOffset, 8, 0, 0, DateTimeKind.Unspecified), Vietnam);

    private static PeriodicMaintenanceReminderStage? StageAt(Ticket ticket, DateTime nowUtc) =>
        PeriodicMaintenanceReminderBackgroundService.GetDueReminderStage(
            ticket, nowUtc, Vietnam, EightAm);

    [Fact]
    public void BeforeEightAm_OnTheDayTheTicketOpened_NothingIsDue()
    {
        // 03:00 giờ Việt Nam — ticket đã mở được một giờ, nhưng chưa tới giờ nhắc.
        var now = EightAmLocal(0).AddHours(-5);
        StageAt(Ticket(), now).Should().BeNull();
    }

    [Fact]
    public void AtEightAm_OnTheDayItOpened_TheFirstReminderIsDue()
    {
        StageAt(Ticket(), EightAmLocal(0))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerFirstReminder);
    }

    [Fact]
    public void TheDayAfter_TheSecondReminderIsDue()
    {
        var ticket = Ticket(reminder1: EightAmLocal(0));
        StageAt(ticket, EightAmLocal(1))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerSecondReminder);
    }

    /// <summary>
    /// Không có mốc thứ ba thì một khách không trả lời sẽ treo việc vô thời hạn.
    /// </summary>
    [Fact]
    public void OnTheThirdDay_TheTicketGoesToTheManager()
    {
        var ticket = Ticket(reminder1: EightAmLocal(0), reminder2: EightAmLocal(1));
        StageAt(ticket, EightAmLocal(2))
            .Should().Be(PeriodicMaintenanceReminderStage.ManagerEscalation);
    }

    [Fact]
    public void OnceAllThreeAreSent_NothingMoreIsDue()
    {
        var ticket = Ticket(
            reminder1: EightAmLocal(0), reminder2: EightAmLocal(1), escalated: EightAmLocal(2));
        StageAt(ticket, EightAmLocal(9)).Should().BeNull();
    }

    /// <summary>
    /// Khách đã chọn giờ thì im lặng ngay, kể cả khi các mốc còn nợ — nhắc tiếp là làm phiền
    /// người vừa trả lời xong.
    /// </summary>
    [Fact]
    public void OnceTheCustomerHasPickedATime_RemindersStop()
    {
        var ticket = Ticket(scheduledStart: new DateTime(2026, 9, 18, 3, 0, 0, DateTimeKind.Utc));
        StageAt(ticket, EightAmLocal(2)).Should().BeNull();
    }

    /// <summary>
    /// Worker chết mấy ngày rồi sống lại: trả về mốc còn nợ SỚM NHẤT, không nhảy thẳng tới
    /// escalate. Khách vẫn phải được nhắc trước khi việc bị lấy khỏi tay họ.
    /// </summary>
    [Fact]
    public void AfterAnOutage_TheEarliestUnsentStageComesFirst()
    {
        StageAt(Ticket(), EightAmLocal(5))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerFirstReminder);
    }

    /// <summary>
    /// Mốc bám ngày địa phương, không phải số giờ trôi qua. Ticket mở lúc 02:00 thì lần nhắc
    /// đầu cách đó 6 giờ; nếu tính "sau 24 giờ" thì nó rơi vào 02:00 sáng hôm sau.
    /// </summary>
    [Fact]
    public void ReminderTimeFollowsTheLocalClock_NotHoursElapsed()
    {
        var sixHoursAfterOpening = new DateTime(2026, 9, 9, 19, 0, 0, DateTimeKind.Utc).AddHours(6);

        sixHoursAfterOpening.Should().Be(EightAmLocal(0));
        StageAt(Ticket(), sixHoursAfterOpening)
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerFirstReminder);
    }

    /// <summary>Ticket bị huỷ mốc giữa chừng vẫn không bỏ qua mốc chưa gửi.</summary>
    [Fact]
    public void ASkippedFirstReminder_IsStillOwedOnTheSecondDay()
    {
        var ticket = Ticket(reminder2: EightAmLocal(1));
        StageAt(ticket, EightAmLocal(1))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerFirstReminder);
    }
}
