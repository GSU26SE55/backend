using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketLifecycleCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<ISlaService> _slaService = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();

    #region TicketReassign
    [Fact]
    public async Task Reassign_ValidRequest_UpdatesStaff()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var oldStaffId = Guid.NewGuid();
        var newStaffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Assigned, PrimaryHandlerStaffId = oldStaffId };
        var newStaffSeed = new List<StaffAccount> { new() { AccountId = newStaffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var command = new TicketReassignCommand { TicketId = ticketId, ManagerId = managerId, NewPrimaryHandlerStaffId = newStaffId, ManagerName = "Mgr" };

        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: newStaffSeed);

        var handler = new TicketReassignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);

        ticket.PrimaryHandlerStaffId.Should().Be(newStaffId);
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Assigned, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reassign_ValidRequest_MovesOldStaffToPreviousAssigneeAndCreatesNewPrimaryAssignee()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var oldStaffId = Guid.NewGuid();
        var newStaffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Assigned, PrimaryHandlerStaffId = oldStaffId };
        var oldParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = oldStaffId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.PrimaryAssignee,
            CanPost = true,
            CanViewInternal = true,
            AddedByUserId = managerId,
            AddedAt = DateTime.UtcNow
        };
        var newStaffSeed2 = new List<StaffAccount> { new() { AccountId = newStaffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var oldAssignmentSeed = new[] { new TicketAssignment { Id = Guid.NewGuid(), TicketId = ticketId, StaffId = oldStaffId, Role = AssignmentRoleEnum.PrimaryHandler } };
        var command = new TicketReassignCommand { TicketId = ticketId, ManagerId = managerId, NewPrimaryHandlerStaffId = newStaffId, ManagerName = "Mgr" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { oldParticipant },
            staffSeed: newStaffSeed2, assignmentSeed: oldAssignmentSeed);

        var handler = new TicketReassignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        oldAssignmentSeed.Single().Role.Should().Be(AssignmentRoleEnum.PreviousPrimaryHandler);
        oldParticipant.ParticipantType.Should().Be(ParticipantTypeEnum.PreviousAssignee);
        oldParticipant.CanPost.Should().BeFalse();

        participants.Verify(x => x.UpdateAsync(oldParticipant), Times.Once);
        participants.Verify(x => x.AddAsync(It.Is<TicketParticipant>(p =>
            p.UserId == newStaffId && p.ParticipantType == ParticipantTypeEnum.PrimaryAssignee)), Times.Once);
    }

    [Fact]
    public async Task Reassign_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var newStaffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Closed };
        var staffSeed = new List<StaffAccount> { new() { AccountId = newStaffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var command = new TicketReassignCommand { TicketId = ticketId, ManagerId = managerId, NewPrimaryHandlerStaffId = newStaffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staffSeed);
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Denied" });

        var handler = new TicketReassignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketStart
    [Fact]
    public async Task Start_ValidRequest_UpdatesStatus()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Assigned };
        var command = new TicketStartCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketStartCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.InProgress, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketStatusChangedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Open };
        var command = new TicketStartCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, staffId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketStartCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketHold
    [Fact]
    public async Task Hold_ValidRequest_UpdatesStatus()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.InProgress };
        var command = new TicketHoldCommand { TicketId = ticketId, StaffId = staffId, Reason = PauseReasonEnum.WaitingParts };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _slaService.Setup(x => x.CheckPauseEligibilityAsync(ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlaPauseEligibility(true));
        var handler = new TicketHoldCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.WaitingParts, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketStatusChangedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketHeldIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hold_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Closed };
        var command = new TicketHoldCommand { TicketId = ticketId, StaffId = staffId, Reason = PauseReasonEnum.WaitingParts };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.WaitingParts, ActorRoleEnum.Staff, staffId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketHoldCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketResume
    [Fact]
    public async Task Resume_ValidRequest_UpdatesStatus()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.WaitingParts };
        var command = new TicketResumeCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResumeCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.InProgress, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketStatusChangedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketResumedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resume_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Open };
        var command = new TicketResumeCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, staffId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketResumeCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketApprove
    [Fact]
    public async Task Approve_ValidRequest_ClosesTicket()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Resolved };
        var command = new TicketApproveCommand { TicketId = ticketId, ManagerId = managerId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketApproveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.ClosedPendingRate, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketApprovedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Open };
        var command = new TicketApproveCommand { TicketId = ticketId, ManagerId = managerId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.ClosedPendingRate, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketApproveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketReject
    [Fact]
    public async Task Reject_ValidRequest_ReturnsToInProgress()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Resolved };
        var command = new TicketRejectCommand { TicketId = ticketId, ManagerId = managerId, Reason = "Not fixed" };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);

        ticket.Reason.Should().Be("Not fixed");
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.InProgress, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketRejectedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Open };
        var command = new TicketRejectCommand { TicketId = ticketId, ManagerId = managerId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketEscalateRequest
    [Fact]
    public async Task EscalateRequest_ValidRequest_UpdatesStatus()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.InProgress };
        var command = new TicketEscalateRequestCommand { TicketId = ticketId, StaffId = staffId, Reason = EscalationReasonEnum.SkillGap };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketEscalateRequestCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);

        ticket.EscalationReason.Should().Be(EscalationReasonEnum.SkillGap);
        ticket.EscalatedAt.Should().NotBeNull();
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Escalated, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketEscalatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EscalateRequest_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Closed };
        var command = new TicketEscalateRequestCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Staff, staffId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketEscalateRequestCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    #region TicketEscalateForce
    [Fact]
    public async Task EscalateForce_ValidRequest_UpdatesStatus()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Assigned };
        var command = new TicketEscalateForceCommand { TicketId = ticketId, ManagerId = managerId, Reason = EscalationReasonEnum.SlaBreach };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketEscalateForceCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Code.Should().Be(ticket.Code);
        result.Data.Status.Should().Be(ticket.Status);

        ticket.EscalationReason.Should().Be(EscalationReasonEnum.SlaBreach);
        ticket.EscalatedAt.Should().NotBeNull();
        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Escalated, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketEscalatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EscalateForce_InvalidTransition_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Title = "T", Description = "D", Status = TicketStatusEnum.Closed };
        var command = new TicketEscalateForceCommand { TicketId = ticketId, ManagerId = managerId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false });

        var handler = new TicketEscalateForceCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var command = new TicketStartCommand { TicketId = ticketId, StaffId = Guid.NewGuid() };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: Enumerable.Empty<Ticket>());

        var handler = new TicketStartCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
