using Microsoft.Extensions.Logging.Abstractions;
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
        var ticket = new Ticket
        {
            Id = timer.TicketId,
            Code = "T-RECONCILE",
            CustomerId = Guid.NewGuid(),
            Title = "Test",
            Description = "Test",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P1Critical,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket],
            slaTimerSeed: [timer],
            slaPauseEventSeed: [pauseEvent]);
        var reconciler = new SlaDeadlineReconciler(uow.Object, calculator, NullLogger<SlaDeadlineReconciler>.Instance);

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

    [Fact]
    public async Task ReconcileActiveTimersAsync_WhenTicketIsOpen_ComputesResponseDueDateAndZeroPause()
    {
        var localDate = new DateOnly(2026, 8, 17);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var startedAt = ToUtc(localDate, new TimeOnly(7, 0), timeZone);

        var calculator = new SlaCalculator(
            Options.Create(new SlaBusinessHoursOptions()),
            new Mock<ISlaBusinessCalendarProvider>().Object);

        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-OPEN-RECONCILE",
            CustomerId = Guid.NewGuid(),
            Title = "Open Ticket",
            Description = "Test",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P1Critical,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Priority = TicketPriorityEnum.P1Critical,
            Status = SlaTimerStatusEnum.Running,
            StartedAt = startedAt,
            OriginalDueAt = startedAt.AddHours(4),
            DueAt = startedAt.AddHours(4),
            TotalPausedMinutes = 0
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket],
            slaTimerSeed: [timer]);
        var reconciler = new SlaDeadlineReconciler(uow.Object, calculator, NullLogger<SlaDeadlineReconciler>.Instance);

        await reconciler.ReconcileActiveTimersAsync();

        timer.OriginalDueAt.Should().Be(startedAt.AddHours(4));
        timer.DueAt.Should().Be(startedAt.AddHours(4));
        timer.TotalPausedMinutes.Should().Be(0);
    }

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified),
            timeZone);
}
