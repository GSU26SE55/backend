using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Services;

public class SlaDeadlineReconcilerTests
{
    [Fact]
    public async Task ReconcileActiveTimersAsync_WhenCalendarChanges_RecomputesCompletedPauseMinutes()
    {
        var localDate = new DateOnly(2026, 8, 17);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var startedAt = ToUtc(localDate, new TimeOnly(7, 0), timeZone);
        var pausedAt = ToUtc(localDate, new TimeOnly(8, 30), timeZone);
        var resumedAt = ToUtc(localDate, new TimeOnly(9, 0), timeZone);
        var isHoliday = true;

        var calendar = new Mock<ISlaBusinessCalendarProvider>();
        calendar.Setup(x => x.IsNonWorkingDate(It.IsAny<DateOnly>()))
            .Returns((DateOnly date) => isHoliday && date == localDate);
        var calculator = new SlaCalculator(
            Options.Create(new SlaBusinessHoursOptions()),
            calendar.Object);

        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            Priority = TicketPriorityEnum.P1Critical,
            Status = SlaTimerStatusEnum.Running,
            StartedAt = startedAt,
            OriginalDueAt = calculator.CalculateDueDate(startedAt, TicketPriorityEnum.P1Critical),
            DueAt = calculator.CalculateDueDate(startedAt, TicketPriorityEnum.P1Critical),
            TotalPausedMinutes = 30
        };
        var pauseEvent = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = timer.Id,
            Reason = PauseReasonEnum.WorkBlocked,
            PausedAt = pausedAt,
            ResumedAt = resumedAt,
            DurationMinutes = 30,
            PausedByUserId = Guid.NewGuid(),
            ResumedByUserId = Guid.NewGuid()
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            slaTimerSeed: [timer],
            slaPauseEventSeed: [pauseEvent]);
        var reconciler = new SlaDeadlineReconciler(uow.Object, calculator);

        await reconciler.ReconcileActiveTimersAsync();

        pauseEvent.DurationMinutes.Should().Be(0);
        timer.TotalPausedMinutes.Should().Be(0);
        timer.DueAt.Should().Be(timer.OriginalDueAt);

        isHoliday = false;
        await reconciler.ReconcileActiveTimersAsync();

        pauseEvent.DurationMinutes.Should().Be(30);
        timer.TotalPausedMinutes.Should().Be(30);
        timer.DueAt.Should().Be(calculator.AddWorkingMinutes(timer.OriginalDueAt, 30));
    }

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified),
            timeZone);
}
