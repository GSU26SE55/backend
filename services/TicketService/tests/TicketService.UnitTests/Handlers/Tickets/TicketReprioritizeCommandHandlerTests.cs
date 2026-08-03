using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketReprioritizeCommandHandlerTests
{
    [Fact]
    public async Task Handle_EligibleRunningTicket_RecalculatesDueDateAndAudits()
    {
        var ticket = Ticket(TicketStatusEnum.InProgress, TicketPriorityEnum.P3Normal);
        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticket.Id, Priority = TicketPriorityEnum.P3Normal, StartedAt = DateTime.UtcNow.AddHours(-2), OriginalDueAt = DateTime.UtcNow.AddHours(70), DueAt = DateTime.UtcNow.AddHours(70), TotalPausedMinutes = 30, Status = SlaTimerStatusEnum.Running };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, slaTimerSeed: new[] { timer });
        var calculator = new Mock<ISlaCalculator>();
        var newOriginalDueAt = timer.StartedAt.AddHours(24);
        calculator.Setup(x => x.CalculateDueDate(timer.StartedAt, TicketPriorityEnum.P2High)).Returns(newOriginalDueAt);
        var logger = new Mock<IActivityLogger>();
        var handler = CreateHandler(uow.Object, calculator.Object, logger.Object, new Mock<IIntegrationEventOutboxWriter>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand { TicketId = ticket.Id, Priority = TicketPriorityEnum.P2High, Reason = "Urgent", ManagerId = Guid.NewGuid(), ManagerName = "Manager" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.P2High);
        timer.OriginalDueAt.Should().Be(newOriginalDueAt);
        timer.DueAt.Should().Be(newOriginalDueAt.AddMinutes(30));
        logger.Verify(x => x.LogAsync(ticket.Id, It.IsAny<Guid>(), ActorRoleEnum.Manager, "Manager", ActivityActionEnum.PriorityAssigned, "P3Normal", "P2High", "Urgent"), Times.Once);
    }

    [Fact]
    public async Task Handle_BreachedTimer_DoesNotResetDeadline()
    {
        var ticket = Ticket(TicketStatusEnum.Escalated, TicketPriorityEnum.P2High);
        var dueAt = DateTime.UtcNow.AddHours(-1);
        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticket.Id, Priority = TicketPriorityEnum.P2High, StartedAt = DateTime.UtcNow.AddHours(-3), OriginalDueAt = dueAt, DueAt = dueAt, Status = SlaTimerStatusEnum.Breached };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, slaTimerSeed: new[] { timer });
        var calculator = new Mock<ISlaCalculator>();
        var handler = CreateHandler(uow.Object, calculator.Object, new Mock<IActivityLogger>().Object, new Mock<IIntegrationEventOutboxWriter>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand { TicketId = ticket.Id, Priority = TicketPriorityEnum.P3Normal, Reason = "Review", ManagerId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        timer.DueAt.Should().Be(dueAt);
        calculator.Verify(x => x.CalculateDueDate(It.IsAny<DateTime>(), It.IsAny<TicketPriorityEnum>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewPastDueDate_MarksTimerBreachedAndWritesEvent()
    {
        var ticket = Ticket(TicketStatusEnum.InProgress, TicketPriorityEnum.P3Normal);
        var timer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticket.Id, Priority = TicketPriorityEnum.P3Normal, StartedAt = DateTime.UtcNow.AddDays(-1), OriginalDueAt = DateTime.UtcNow, DueAt = DateTime.UtcNow, Status = SlaTimerStatusEnum.Running };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, slaTimerSeed: new[] { timer });
        var calculator = new Mock<ISlaCalculator>();
        calculator.Setup(x => x.CalculateDueDate(timer.StartedAt, TicketPriorityEnum.P2High)).Returns(DateTime.UtcNow.AddMinutes(-1));
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var handler = CreateHandler(uow.Object, calculator.Object, new Mock<IActivityLogger>().Object, outbox.Object);

        await handler.Handle(new TicketReprioritizeCommand { TicketId = ticket.Id, Priority = TicketPriorityEnum.P2High, Reason = "Urgent" }, CancellationToken.None);

        timer.Status.Should().Be(SlaTimerStatusEnum.Breached);
        timer.BreachAt.Should().NotBeNull();
        outbox.Verify(x => x.WriteAsync(It.Is<SlaBreachedEvent>(e => e.TicketId == ticket.Id && e.Priority == "P2High"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NewTicket_IsRejected()
    {
        var ticket = Ticket(TicketStatusEnum.New, TicketPriorityEnum.P3Normal);
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object, new Mock<IIntegrationEventOutboxWriter>().Object);
        var result = await handler.Handle(new TicketReprioritizeCommand { TicketId = ticket.Id, Priority = TicketPriorityEnum.P2High, Reason = "Review" }, CancellationToken.None);
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ValidateAsync_InvalidReprioritizeRequest_ReturnsValidationErrors()
    {
        var command = new TicketReprioritizeCommand
        {
            TicketId = Guid.Empty,
            Priority = (TicketPriorityEnum)999,
            Reason = " "
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_SoftDeletedTicket_IsExcluded()
    {
        var ticket = Ticket(TicketStatusEnum.InProgress, TicketPriorityEnum.P3Normal);
        ticket.IsDeleted = true;
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object, new Mock<IIntegrationEventOutboxWriter>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Priority = TicketPriorityEnum.P2High,
            Reason = "Priority reassessment"
        }, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ReprioritizedTicketWithInsufficientPrimaryTier_EscalatesAndDemotesPrimaryHandlerToPreviousPrimarySupporter()
    {
        var ticket = Ticket(TicketStatusEnum.InProgress, TicketPriorityEnum.P3Normal);
        var tierOneStaffId = Guid.NewGuid();
        var primaryAssignment = new TicketAssignment { Id = Guid.NewGuid(), TicketId = ticket.Id, StaffId = tierOneStaffId, Role = AssignmentRoleEnum.PrimaryHandler };
        var staff = new StaffAccount { Id = Guid.NewGuid(), AccountId = tierOneStaffId, Email = "tier1@test.com", FullName = "Tier 1", Status = AccountStatusEnum.Active, SkillTier = StaffSkillTierEnum.Generalist };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: new[] { staff }, assignmentSeed: new[] { primaryAssignment });
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var stateMachine = new Mock<TicketService.Application.StateMachine.ITicketStateMachine>();
        stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Manager, It.IsAny<Guid>()))
            .Returns(new TicketService.Application.StateMachine.TransitionResult { IsAllowed = true });
        stateMachine.Setup(x => x.ExecuteAsync(ticket, TicketStatusEnum.Escalated, It.IsAny<TicketService.Application.StateMachine.TransitionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                ticket.Status = TicketStatusEnum.Escalated;
                ticket.EscalationReason = EscalationReasonEnum.SkillGap;
                return Task.FromResult(new TicketService.Application.StateMachine.TransitionResult { IsAllowed = true });
            });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object, outbox.Object, stateMachine.Object);

        var result = await handler.Handle(new TicketReprioritizeCommand { TicketId = ticket.Id, Priority = TicketPriorityEnum.P2High, Reason = "Impact is higher than initial triage.", ManagerId = Guid.NewGuid(), ManagerName = "Manager" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Escalated);
        ticket.EscalationReason.Should().Be(EscalationReasonEnum.SkillGap);
        primaryAssignment.Role.Should().Be(AssignmentRoleEnum.PreviousPrimaryHandler);
        outbox.Verify(x => x.WriteAsync(It.Is<TicketService.Application.IntegrationEvents.TicketEscalatedIntegrationEvent>(e => e.TicketId == ticket.Id && e.Reason == EscalationReasonEnum.SkillGap), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TicketReprioritizeCommandHandler CreateHandler(
        TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork uow,
        ISlaCalculator calculator,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter outboxWriter,
        TicketService.Application.StateMachine.ITicketStateMachine? stateMachine = null)
        => new(uow, calculator, activityLogger, outboxWriter, stateMachine ?? new Mock<TicketService.Application.StateMachine.ITicketStateMachine>().Object, new Mock<MediatR.IPublisher>().Object);

    private static Ticket Ticket(TicketStatusEnum status, TicketPriorityEnum priority) => new() { Id = Guid.NewGuid(), Code = "TKT-1", Title = "Test", Description = "Test", Status = status, Priority = priority };
}
