using FluentAssertions;
using MassTransit;
using Moq;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Infrastructure.BackgroundJobs;

public class EscalationBackgroundServiceTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<IInboxStore> _inboxStore = new();
    private readonly Mock<ITicketActivationService> _slaTransitions = new();

    public EscalationBackgroundServiceTests()
    {
        _inboxStore.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "token"));
        _inboxStore.Setup(i => i.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static ConsumeContext<SlaBreachedEvent> CreateContext(Guid ticketId, DateTime breachedAt, string code = "TKT-001", string priority = "P3Normal")
    {
        var msg = new SlaBreachedEvent
        {
            TicketId = ticketId,
            BreachedAt = breachedAt,
            Code = code,
            Priority = priority
        };
        var mock = new Mock<ConsumeContext<SlaBreachedEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        mock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    [Fact]
    public async Task Consume_OpenTicket_P3Breached_BumpsToP2_KeepsOpenStatus_NoStateMachineTransition()
    {
        var ticketId = Guid.NewGuid();
        var breachedAt = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-OPEN-P3",
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P3Normal,
            Title = "Open Ticket",
            Description = "Desc",
            Category = TicketCategoryEnum.Other,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Priority = TicketPriorityEnum.P3Normal,
            Status = SlaTimerStatusEnum.Breached,
            StartedAt = breachedAt.AddHours(-73),
            DueAt = breachedAt.AddHours(-1)
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket],
            slaTimerSeed: [timer]);

        var service = new EscalationBackgroundService(
            uow.Object,
            _stateMachine.Object,
            _activityLogger.Object,
            _outboxWriter.Object,
            _inboxStore.Object,
            _slaTransitions.Object);

        await service.Consume(CreateContext(ticketId, breachedAt, "TKT-OPEN-P3", "P3Normal"));

        ticket.Status.Should().Be(TicketStatusEnum.Open, "Open tickets must NOT transition to ReAssign on breach");
        ticket.Priority.Should().Be(TicketPriorityEnum.P2High);
        ticket.EscalationReason.Should().Be(EscalationReasonEnum.SlaBreach);
        ticket.EscalatedAt.Should().Be(breachedAt);

        _stateMachine.Verify(
            s => s.ExecuteAsync(It.IsAny<Ticket>(), It.IsAny<TicketStatusEnum>(), It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _outboxWriter.Verify(
            o => o.WriteAsync(It.Is<TicketEscalatedEvent>(e => e.TicketId == ticketId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_OpenTicket_P1Breached_DeclaresUrgentIncident_StopsSla_EmitsBatteryIsolation()
    {
        var ticketId = Guid.NewGuid();
        var batteryAssetId = Guid.NewGuid();
        var breachedAt = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-OPEN-P1",
            Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P1Critical,
            BatteryAssetId = batteryAssetId,
            Title = "Open Ticket Critical",
            Description = "Desc",
            Category = TicketCategoryEnum.Repair,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Priority = TicketPriorityEnum.P1Critical,
            Status = SlaTimerStatusEnum.Breached,
            StartedAt = breachedAt.AddHours(-5),
            DueAt = breachedAt.AddHours(-1)
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket],
            slaTimerSeed: [timer]);

        var service = new EscalationBackgroundService(
            uow.Object,
            _stateMachine.Object,
            _activityLogger.Object,
            _outboxWriter.Object,
            _inboxStore.Object,
            _slaTransitions.Object);

        await service.Consume(CreateContext(ticketId, breachedAt, "TKT-OPEN-P1", "P1Critical"));

        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.Priority.Should().Be(TicketPriorityEnum.Urgent);
        ticket.IsIncident.Should().BeTrue();
        ticket.ActiveIncidentEpisodeId.Should().NotBeNull();

        _slaTransitions.Verify(s => s.StopSlaAsync(ticket, It.IsAny<CancellationToken>()), Times.Once);

        _outboxWriter.Verify(
            o => o.WriteAsync(
                It.Is<BatteryIsolationRequestedEvent>(e =>
                    e.TicketId == ticketId &&
                    e.BatteryAssetIds.Contains(batteryAssetId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Consume_InProgressTicket_P3Breached_BumpsToP2_TransitionsToReAssign_DemotesPrimary()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var breachedAt = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-INP-P3",
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P3Normal,
            Title = "InProgress Ticket",
            Description = "Desc",
            Category = TicketCategoryEnum.Repair,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Type = SlaTimerTypeEnum.Resolution,
            Priority = TicketPriorityEnum.P3Normal,
            Status = SlaTimerStatusEnum.Breached,
            StartedAt = breachedAt.AddDays(-3),
            DueAt = breachedAt.AddHours(-1)
        };
        var assignment = new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: [ticket],
            slaTimerSeed: [timer],
            assignmentSeed: [assignment]);

        _stateMachine.Setup(s => s.ExecuteAsync(ticket, TicketStatusEnum.ReAssign, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        var service = new EscalationBackgroundService(
            uow.Object,
            _stateMachine.Object,
            _activityLogger.Object,
            _outboxWriter.Object,
            _inboxStore.Object,
            _slaTransitions.Object);

        await service.Consume(CreateContext(ticketId, breachedAt, "TKT-INP-P3", "P3Normal"));

        ticket.Priority.Should().Be(TicketPriorityEnum.P2High);
        assignment.Role.Should().Be(AssignmentRoleEnum.PreviousPrimaryHandler);

        _stateMachine.Verify(
            s => s.ExecuteAsync(ticket, TicketStatusEnum.ReAssign, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
