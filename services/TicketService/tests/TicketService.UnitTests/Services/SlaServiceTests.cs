using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Helpers;
using TicketService.UnitTests.Helpers;

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

        var service = new SlaService(uow.Object);

        // Act
        await service.PauseSlaAsync(ticketId, PauseReasonEnum.WaitingParts, "Note", userId, CancellationToken.None);

        // Assert
        slaTimer.Status.Should().Be(SlaTimerStatusEnum.Paused);
        slaTimer.CurrentPauseStartedAt.Should().NotBeNull();
        slaTimer.PauseEpisodesCount.Should().Be(1);

        pauseEvents.Should().HaveCount(1);
        pauseEvents[0].Reason.Should().Be(PauseReasonEnum.WaitingParts);
        pauseEvents[0].Note.Should().Be("Note");
        pauseEvents[0].PausedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task ResumeSlaAsync_WithPausedSlaTimer_ResumesTimerAndUpdatesEvent()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var slaTimer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Status = SlaTimerStatusEnum.Paused,
            DueAt = DateTime.UtcNow.AddHours(4),
            CurrentPauseStartedAt = DateTime.UtcNow.AddMinutes(-30),
            TotalPausedMinutes = 0
        };

        var pauseEvent = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = slaTimer.Id,
            Reason = PauseReasonEnum.WaitingParts,
            PausedAt = DateTime.UtcNow.AddMinutes(-30),
            PausedByUserId = userId
        };

        var (uow, _, _, _, _, _, slaPauseEventsRepo) = MockTicketUnitOfWork.Build(
            slaTimerSeed: new[] { slaTimer },
            slaPauseEventSeed: new[] { pauseEvent });

        var service = new SlaService(uow.Object);

        // Act
        await service.ResumeSlaAsync(ticketId, userId, CancellationToken.None);

        // Assert
        slaTimer.Status.Should().Be(SlaTimerStatusEnum.Running);
        slaTimer.CurrentPauseStartedAt.Should().BeNull();
        slaTimer.TotalPausedMinutes.Should().BeGreaterThan(0);
        slaTimer.DueAt.Should().BeAfter(DateTime.UtcNow.AddHours(3).AddMinutes(59));

        pauseEvent.ResumedAt.Should().NotBeNull();
        pauseEvent.ResumedByUserId.Should().Be(userId);
        pauseEvent.DurationMinutes.Should().BeGreaterThan(0);
    }
}
