using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Services;

public class SlaServiceTests
{
    [Fact]
    public async Task PauseSlaAsync_WithActiveSlaTimer_PausesTimerAndCreatesEvent()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var slaTimer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Status = SlaTimerStatusEnum.Running,
            DueAt = DateTime.UtcNow.AddHours(4)
        };

        var (uow, _, _, _, _, slaTimersRepo, slaPauseEventsRepo) = MockTicketUnitOfWork.Build(
            slaTimerSeed: new[] { slaTimer });

        var pauseEvents = new List<SlaPauseEvent>();
        slaPauseEventsRepo.Setup(x => x.AddAsync(It.IsAny<SlaPauseEvent>()))
            .Callback<SlaPauseEvent>(e => pauseEvents.Add(e))
            .Returns(Task.CompletedTask);

        var service = new SlaService(uow.Object, new SlaCalculator());

        // Act
        await service.PauseSlaAsync(ticketId, PauseReasonEnum.WorkBlocked, "Note", userId, CancellationToken.None);

        // Assert
        slaTimer.Status.Should().Be(SlaTimerStatusEnum.Paused);
        slaTimer.CurrentPauseStartedAt.Should().NotBeNull();
        slaTimer.PauseEpisodesCount.Should().Be(1);

        pauseEvents.Should().HaveCount(1);
        pauseEvents[0].Reason.Should().Be(PauseReasonEnum.WorkBlocked);
        pauseEvents[0].Note.Should().Be("Note");
        pauseEvents[0].PausedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task ResumeSlaAsync_WithPausedSlaTimer_ResumesTimerAndUpdatesEvent()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var resumedAt = new DateTime(2026, 8, 17, 2, 0, 0, DateTimeKind.Utc); // 09:00 local
        var pausedAt = resumedAt.AddMinutes(-30); // 08:30 local
        var originalDueAt = resumedAt.AddHours(4);

        var slaTimer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Status = SlaTimerStatusEnum.Paused,
            DueAt = originalDueAt,
            CurrentPauseStartedAt = pausedAt,
            TotalPausedMinutes = 0
        };

        var pauseEvent = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = slaTimer.Id,
            Reason = PauseReasonEnum.WorkBlocked,
            PausedAt = pausedAt,
            PausedByUserId = userId
        };

        var (uow, _, _, _, _, _, slaPauseEventsRepo) = MockTicketUnitOfWork.Build(
            slaTimerSeed: new[] { slaTimer },
            slaPauseEventSeed: new[] { pauseEvent });

        var calculator = new SlaCalculator();
        var service = new SlaService(
            uow.Object,
            calculator,
            new FixedTimeProvider(resumedAt));

        // Act
        await service.ResumeSlaAsync(ticketId, userId, CancellationToken.None);

        // Assert
        slaTimer.Status.Should().Be(SlaTimerStatusEnum.Running);
        slaTimer.CurrentPauseStartedAt.Should().BeNull();
        slaTimer.TotalPausedMinutes.Should().Be(30);
        slaTimer.DueAt.Should().Be(calculator.AddWorkingMinutes(originalDueAt, 30));

        pauseEvent.ResumedAt.Should().Be(resumedAt);
        pauseEvent.ResumedByUserId.Should().Be(userId);
        pauseEvent.DurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task ResumeSlaAsync_PauseAcrossWeekend_CountsWeekendWorkingMinutes()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var pausedAt = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc); // Friday 16:00 local
        var resumedAt = new DateTime(2026, 8, 24, 2, 0, 0, DateTimeKind.Utc); // Monday 09:00 local
        var originalDueAt = new DateTime(2026, 8, 24, 4, 0, 0, DateTimeKind.Utc); // Monday 11:00 local
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Priority = TicketPriorityEnum.P1Critical,
            Status = SlaTimerStatusEnum.Paused,
            DueAt = originalDueAt,
            CurrentPauseStartedAt = pausedAt
        };
        var pauseEvent = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = timer.Id,
            Reason = PauseReasonEnum.WorkBlocked,
            PausedAt = pausedAt,
            PausedByUserId = userId
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            slaTimerSeed: new[] { timer },
            slaPauseEventSeed: new[] { pauseEvent });
        var service = new SlaService(
            uow.Object,
            new SlaCalculator(),
            new FixedTimeProvider(resumedAt));

        await service.ResumeSlaAsync(ticketId, userId, CancellationToken.None);

        pauseEvent.DurationMinutes.Should().Be(1380);
        timer.TotalPausedMinutes.Should().Be(1380);
        timer.DueAt.Should().Be(new SlaCalculator().AddWorkingMinutes(originalDueAt, 1380));
        timer.PauseEpisodesCount.Should().Be(0);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
