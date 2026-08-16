using FluentAssertions;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.IntegrationEvents;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

/// <summary>
/// Unit test cho luồng Staff yêu cầu escalate ticket lên Manager.
/// Mỗi test tương ứng 1 UTCID trong GSU26SE55_Unit_Test_Report.xlsx — sheet TicketEscalateRequest.
/// </summary>
public class TicketEscalateRequestCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ISlaService> _slaService = new();

    private TicketEscalateRequestCommandHandler CreateHandler(Mock<TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork> uow)
        => new(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object,
            Mock.Of<MediatR.IPublisher>(), _slaService.Object);

    private static Ticket BuildTicket(Guid ticketId, TicketPriorityEnum priority = TicketPriorityEnum.P3Normal)
        => new()
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            Status = TicketStatusEnum.InProgress,
            Priority = priority
        };

    private static TicketAssignment BuildPrimaryAssignment(Guid ticketId, Guid staffId)
        => new()
        {
            TicketId = ticketId,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        };

    /// <summary>UTCID01 — Happy path: PrimaryHandler hợp lệ, note có nội dung, transition được phép.</summary>
    [Fact]
    public async Task Handle_ValidRequest_SubmitsEscalationAndPausesSla()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { BuildPrimaryAssignment(ticketId, staffId) });

        var command = new TicketEscalateRequestCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            StaffName = "Staff A",
            Reason = EscalationReasonEnum.SkillGap,
            Note = "Cần chuyên gia tier cao hơn"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Message.Should().Be("Escalation request submitted for Manager review.");
        ticket.Status.Should().Be(TicketStatusEnum.Request);
        ticket.EscalationReason.Should().Be(EscalationReasonEnum.SkillGap);
        ticket.EscalatedAt.Should().NotBeNull();
        ticket.Reason.Should().Be("Cần chuyên gia tier cao hơn");

        _slaService.Verify(x => x.PauseSlaAsync(ticketId, PauseReasonEnum.WorkBlocked,
            "Cần chuyên gia tier cao hơn", staffId, It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(ticketId, staffId, ActorRoleEnum.Staff, "Staff A",
            ActivityActionEnum.EscalationRequested, null, null, "Cần chuyên gia tier cao hơn"), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketEscalatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>UTCID02 — Ticket không tồn tại → 404.</summary>
    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();

        var command = new TicketEscalateRequestCommand
        {
            TicketId = Guid.NewGuid(),
            StaffId = Guid.NewGuid(),
            Note = "Note"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Ticket not found.");
    }

    /// <summary>UTCID03 — Ticket đã ở priority Urgent (cao nhất) → 409, không thể escalate thêm.</summary>
    [Fact]
    public async Task Handle_UrgentPriority_Returns409()
    {
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId, TicketPriorityEnum.Urgent);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketEscalateRequestCommand
        {
            TicketId = ticketId,
            StaffId = Guid.NewGuid(),
            Note = "Note"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Urgent is the highest priority and cannot be escalated.");
    }

    /// <summary>UTCID04 — Người gọi không phải PrimaryHandler đang active → 403.</summary>
    [Fact]
    public async Task Handle_CallerIsNotPrimaryHandler_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var primaryStaffId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { BuildPrimaryAssignment(ticketId, primaryStaffId) });

        var command = new TicketEscalateRequestCommand
        {
            TicketId = ticketId,
            StaffId = otherStaffId,
            Note = "Note"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Only the active PrimaryHandler can request escalation.");
    }

    /// <summary>UTCID05 — Note rỗng/chỉ khoảng trắng → 400 (bắt buộc có lý do escalate).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_MissingNote_Returns400(string? note)
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { BuildPrimaryAssignment(ticketId, staffId) });

        var command = new TicketEscalateRequestCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            Note = note
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("An escalation reason note is required.");
    }

    /// <summary>UTCID06 — State machine chặn transition sang Request → 409 kèm lý do từ state machine.</summary>
    [Fact]
    public async Task Handle_TransitionNotAllowed_Returns409WithStateMachineReason()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { BuildPrimaryAssignment(ticketId, staffId) });

        _stateMachine
            .Setup(x => x.CanTransition(It.IsAny<Ticket>(), TicketStatusEnum.Request,
                ActorRoleEnum.Staff, It.IsAny<Guid>()))
            .Returns(new TransitionResult
            {
                IsAllowed = false,
                Reason = "Ticket đã Completed, không thể escalate."
            });

        var command = new TicketEscalateRequestCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            Note = "Note"
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Be("Ticket đã Completed, không thể escalate.");
        _slaService.Verify(x => x.PauseSlaAsync(It.IsAny<Guid>(), It.IsAny<PauseReasonEnum>(),
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
