using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

/// <summary>
/// Unit test cho luồng Manager duyệt/từ chối yêu cầu escalate.
/// Mỗi test tương ứng 1 UTCID trong GSU26SE55_Unit_Test_Report.xlsx — sheet TicketEscalationDecision.
/// </summary>
public class TicketEscalationDecisionCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<ISlaService> _slaService = new();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketActivationService> _slaTransitions = new();

    private TicketEscalationDecisionCommandHandler CreateHandler(Mock<ITicketUnitOfWork> uow)
        => new(uow.Object, _stateMachine.Object, _slaService.Object, _logger.Object,
            _outboxWriter.Object, _slaTransitions.Object);

    private static Ticket BuildTicket(Guid ticketId,
        TicketPriorityEnum priority = TicketPriorityEnum.P3Normal,
        TicketStatusEnum status = TicketStatusEnum.Request)
        => new()
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            Status = status,
            Priority = priority
        };

    private static TicketAssignment BuildPrimaryAssignment(Guid ticketId, Guid staffId)
        => new()
        {
            TicketId = ticketId,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        };

    /// <summary>UTCID01 — Approve từ P3Normal: priority leo lên P2High, ticket sang ReAssign.</summary>
    [Fact]
    public async Task Handle_ApproveFromP3Normal_EscalatesToP2HighAndReAssign()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.P3Normal);
        var primary = BuildPrimaryAssignment(ticketId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { primary });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Cần tier cao hơn",
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager A"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Message.Should().Be("Escalation approved; ticket requires reassignment.");
        ticket.Priority.Should().Be(TicketPriorityEnum.P2High);
        ticket.Status.Should().Be(TicketStatusEnum.ReAssign);
        primary.Role.Should().Be(AssignmentRoleEnum.PreviousPrimaryHandler);

        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketEscalatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _slaTransitions.Verify(x => x.StopSlaAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>UTCID02 — Ticket không tồn tại → 404.</summary>
    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = Guid.NewGuid(),
            Approve = true,
            Reason = "Reason",
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Ticket not found.");
    }

    /// <summary>UTCID03 — Ticket không ở trạng thái Request (không chờ quyết định) → 409.</summary>
    [Fact]
    public async Task Handle_TicketNotAwaitingDecision_Returns409()
    {
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, status: TicketStatusEnum.InProgress);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Reason",
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Ticket is not awaiting an escalation decision.");
    }

    /// <summary>UTCID04 — Reject: ticket quay lại InProgress, SLA được resume, priority giữ nguyên.</summary>
    [Fact]
    public async Task Handle_Reject_ResumesSlaAndReturnsToInProgress()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.P3Normal);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = false,
            Reason = "Không đủ căn cứ escalate",
            ManagerId = managerId,
            ManagerName = "Manager A"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Escalation rejected; work resumed.");
        ticket.Status.Should().Be(TicketStatusEnum.InProgress);
        ticket.Priority.Should().Be(TicketPriorityEnum.P3Normal);

        _slaService.Verify(x => x.ResumeSlaAsync(ticketId, managerId, It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketEscalatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>UTCID05 — Approve từ P2High: priority leo lên P1Critical.</summary>
    [Fact]
    public async Task Handle_ApproveFromP2High_EscalatesToP1Critical()
    {
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.P2High);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { BuildPrimaryAssignment(ticketId, Guid.NewGuid()) });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Escalate tiếp",
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _slaTransitions.Verify(x => x.StopSlaAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>UTCID06 — State machine chặn transition → 409 kèm lý do từ state machine.</summary>
    [Fact]
    public async Task Handle_TransitionNotAllowed_Returns409WithStateMachineReason()
    {
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine
            .Setup(x => x.CanTransition(It.IsAny<Ticket>(), It.IsAny<TicketStatusEnum>(),
                ActorRoleEnum.Manager, It.IsAny<Guid>()))
            .Returns(new TransitionResult
            {
                IsAllowed = false,
                Reason = "Transition bị chặn bởi state machine."
            });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Reason",
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Transition bị chặn bởi state machine.");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// UTCID07 — Defensive code: ticket đã Urgent mà vẫn approve → ném InvalidOperationException.
    /// Về lý thuyết bước kiểm tra trạng thái đã chặn, đây là nhánh bảo vệ trong switch leo priority.
    /// </summary>
    [Fact]
    public async Task Handle_ApproveWhenAlreadyUrgent_ThrowsInvalidOperationException()
    {
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.Urgent);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Reason",
            ManagerId = Guid.NewGuid()
        };

        var act = async () => await CreateHandler(uow).Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Urgent tickets cannot be escalated.");
    }

    /// <summary>
    /// UTCID08 — Approve từ P1Critical lên Urgent, KeepCurrentPrimary=true và staff đủ tier:
    /// giữ nguyên PrimaryHandler, đồng thời dừng SLA.
    /// </summary>
    [Fact]
    public async Task Handle_ApproveToUrgent_KeepCurrentPrimaryWithSufficientTier_KeepsPrimaryAndStopsSla()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.P1Critical);
        var primary = BuildPrimaryAssignment(ticketId, staffId);
        var staff = new StaffAccount { AccountId = staffId, SkillTier = StaffSkillTierEnum.SeniorSpecialist };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff },
            assignmentSeed: new[] { primary });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Nâng lên Urgent, giữ nguyên người xử lý",
            KeepCurrentPrimary = true,
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.Urgent);
        primary.Role.Should().Be(AssignmentRoleEnum.PrimaryHandler);
        _slaTransitions.Verify(x => x.StopSlaAsync(ticket, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// UTCID09 — Approve từ P1Critical lên Urgent nhưng KeepCurrentPrimary=false:
    /// PrimaryHandler cũ bị chuyển thành PreviousPrimaryHandler.
    /// </summary>
    [Fact]
    public async Task Handle_ApproveToUrgent_WithoutKeepPrimary_DemotesPreviousPrimary()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.P1Critical);
        var primary = BuildPrimaryAssignment(ticketId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { primary });

        var command = new TicketEscalationDecisionCommand
        {
            TicketId = ticketId,
            Approve = true,
            Reason = "Nâng lên Urgent, cần người khác xử lý",
            KeepCurrentPrimary = false,
            ManagerId = Guid.NewGuid()
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.Urgent);
        primary.Role.Should().Be(AssignmentRoleEnum.PreviousPrimaryHandler);
        _slaTransitions.Verify(x => x.StopSlaAsync(ticket, It.IsAny<CancellationToken>()), Times.Once);
    }
}
