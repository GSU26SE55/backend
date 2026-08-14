using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Common;

public class TicketActivationServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Activate_ScheduledReassignmentWithPausedPreviousCycle_ResetsFullCycle()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);

        var result = await fixture.Service.ActivateAsync(
            fixture.Request(ActivationReason.ScheduledDue), CancellationToken.None);

        result.Activated.Should().BeTrue();
        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Running);
        fixture.Timer.StartedAt.Should().Be(NowUtc);
        fixture.Timer.DueAt.Should().Be(NowUtc.AddHours(72));
        fixture.Pause.ResumedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(ActivationReason.ScheduledDue)]
    [InlineData(ActivationReason.EarlyResume)]
    public async Task Activate_HeldTicket_ResumesRemainingCycle(ActivationReason reason)
    {
        var fixture = CreateFixture(PendingContextEnum.Held);
        var originalDueAt = fixture.Timer.DueAt;

        var result = await fixture.Service.ActivateAsync(fixture.Request(reason), CancellationToken.None);

        result.Activated.Should().BeTrue();
        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Running);
        fixture.Timer.StartedAt.Should().Be(NowUtc.AddHours(-10));
        fixture.Timer.DueAt.Should().Be(originalDueAt.AddHours(2));
        fixture.Pause.ResumedAt.Should().Be(NowUtc);
    }

    [Fact]
    public async Task Activate_EarlyResume_PersistsUserReasonInActivity()
    {
        var fixture = CreateFixture(PendingContextEnum.Held);

        var result = await fixture.Service.ActivateAsync(
            fixture.Request(ActivationReason.EarlyResume, "Customer is available now."), CancellationToken.None);

        result.Activated.Should().BeTrue();
        fixture.Activity.Verify(x => x.LogAsync(
            fixture.Ticket.Id,
            fixture.StaffId,
            ActorRoleEnum.Staff,
            It.IsAny<string?>(),
            ActivityActionEnum.StatusChanged,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            "Customer is available now."), Times.Once);
    }

    [Fact]
    public async Task Activate_StaleScheduleVersion_DoesNotMutateTicketOrTimer()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);
        var stale = fixture.Request(ActivationReason.ScheduledDue) with { ExpectedScheduleVersion = 2 };

        var result = await fixture.Service.ActivateAsync(stale, CancellationToken.None);

        result.Activated.Should().BeFalse();
        result.Conflict.Should().Contain("stale");
        fixture.Ticket.Status.Should().Be(TicketStatusEnum.Pending);
        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Paused);
    }

    [Fact]
    public async Task CompleteSla_RunningCycle_MarksMet()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);
        fixture.Timer.Status = SlaTimerStatusEnum.Running;

        await fixture.Service.CompleteSlaAsync(fixture.Ticket, CancellationToken.None);

        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Met);
        fixture.Timer.CurrentPauseStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task StopSla_ExistingCycle_MarksStopped()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);

        await fixture.Service.StopSlaAsync(fixture.Ticket, CancellationToken.None);

        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Stopped);
        fixture.Timer.CurrentPauseStartedAt.Should().BeNull();
    }

    [Fact]
    public async Task StartCorrectionSla_NormalPriority_ResetsFullCycle()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);
        fixture.Timer.Status = SlaTimerStatusEnum.Met;

        await fixture.Service.StartCorrectionSlaAsync(fixture.Ticket, NowUtc, CancellationToken.None);

        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Running);
        fixture.Timer.StartedAt.Should().Be(NowUtc);
        fixture.Timer.DueAt.Should().Be(NowUtc.AddHours(72));
    }

    [Fact]
    public async Task StartCorrectionSla_UrgentPriority_RemainsStopped()
    {
        var fixture = CreateFixture(PendingContextEnum.Scheduled);
        fixture.Ticket.Priority = TicketPriorityEnum.Urgent;
        fixture.Timer.Status = SlaTimerStatusEnum.Met;

        await fixture.Service.StartCorrectionSlaAsync(fixture.Ticket, NowUtc, CancellationToken.None);

        fixture.Timer.Status.Should().Be(SlaTimerStatusEnum.Stopped);
    }

    private static ActivationFixture CreateFixture(PendingContextEnum context)
    {
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-1176",
            Title = "Activation",
            Description = "Activation test",
            CustomerId = Guid.NewGuid(),
            Status = TicketStatusEnum.Pending,
            Priority = TicketPriorityEnum.P3Normal,
            PendingContext = context,
            ScheduledStartAtUtc = NowUtc,
            ScheduleVersion = 3,
            PrimaryHandlerStaffId = staffId
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = NowUtc.AddHours(-10),
            DueAt = NowUtc.AddHours(62),
            OriginalDueAt = NowUtc.AddHours(62),
            CurrentPauseStartedAt = NowUtc.AddHours(-2),
            Status = SlaTimerStatusEnum.Paused
        };
        var pause = new SlaPauseEvent
        {
            Id = Guid.NewGuid(),
            SlaTimerId = timer.Id,
            Reason = PauseReasonEnum.CustomerUnavailable,
            PausedAt = NowUtc.AddHours(-2),
            PausedByUserId = staffId
        };
        var staff = new StaffAccount
        {
            AccountId = staffId,
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            SkillTier = StaffSkillTierEnum.Generalist
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff },
            slaTimerSeed: new[] { timer },
            slaPauseEventSeed: new[] { pause });
        var activity = new Mock<IActivityLogger>();
        activity.Setup(x => x.LogAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string?>(),
                It.IsAny<ActivityActionEnum>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(x => x.WriteAsync(It.IsAny<TicketWorkStartedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new TicketActivationService(
            uow.Object,
            new TicketStateMachine(new TransitionRuleProvider()),
            new SlaCalculator(),
            activity.Object,
            outbox.Object);

        return new ActivationFixture(service, ticket, timer, pause, staffId, activity);
    }

    private record ActivationFixture(
        TicketActivationService Service,
        Ticket Ticket,
        SlaTimer Timer,
        SlaPauseEvent Pause,
        Guid StaffId,
        Mock<IActivityLogger> Activity)
    {
        public ActivationRequest Request(ActivationReason reason, string? userReason = null) => new(
            Ticket,
            StaffId,
            Ticket.ScheduleVersion,
            NowUtc,
            reason,
            StaffId,
            reason == ActivationReason.ScheduledDue ? ActorRoleEnum.System : ActorRoleEnum.Staff,
            "Test actor",
            userReason);
    }
}
