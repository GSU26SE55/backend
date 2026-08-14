using FluentAssertions;
using Moq;
using SharedContracts.Events;
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
/// Staff xin chuyển cấp (§3.12) — Manager và Admin phải nhận được thông báo.
///
/// Thông báo đó chỉ tới nơi nếu outbox nhận đúng <see cref="TicketEscalatedEvent"/> của
/// SharedContracts: OutboxRelayService publish theo runtime type để MassTransit định tuyến
/// exchange, và NotificationService chỉ đăng ký <c>IConsumer&lt;TicketEscalatedEvent&gt;</c>.
/// Ghi một kiểu nội bộ của TicketService thì message vẫn publish thành công, không có lỗi nào
/// hiện ra, nhưng không exchange nào có người nhận — thông báo im lặng biến mất. Bài kiểm tra
/// này khoá đúng chỗ đó lại.
/// </summary>
public class TicketEscalateRequestCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketStateMachine> _stateMachine = new();
    private readonly Mock<ISlaService> _slaService = new();

    [Fact]
    public async Task Handle_ValidRequest_WritesSharedContractEscalatedEvent()
    {
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P2High
        };
        var assignment = new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        };

        var handler = BuildHandler(ticket, assignment, staffId);

        var result = await handler.Handle(new TicketEscalateRequestCommand
        {
            TicketId = ticket.Id,
            Reason = EscalationReasonEnum.SkillGap,
            Note = "  Beyond my skill tier  ",
            StaffId = staffId,
            StaffName = "Staff Test"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<TicketEscalatedEvent>(e =>
                e.TicketId == ticket.Id &&
                e.Code == ticket.Code &&
                e.Reason == (int)EscalationReasonEnum.SkillGap &&
                e.Note == "Beyond my skill tier" &&
                e.StaffId == staffId &&
                e.StaffName == "Staff Test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Kiểu nội bộ <c>TicketEscalatedIntegrationEvent</c> không có consumer nào ở bất kỳ service
    /// nào. Ghi nó ra outbox là đánh rơi thông báo, nên phải khẳng định KHÔNG bao giờ ghi.
    /// </summary>
    [Fact]
    public async Task Handle_ValidRequest_DoesNotWriteConsumerlessInternalEvent()
    {
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-002",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P3Normal
        };
        var assignment = new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            StaffId = staffId,
            Role = AssignmentRoleEnum.PrimaryHandler
        };

        var handler = BuildHandler(ticket, assignment, staffId);

        await handler.Handle(new TicketEscalateRequestCommand
        {
            TicketId = ticket.Id,
            Reason = EscalationReasonEnum.CustomerComplaint,
            Note = "Customer escalated",
            StaffId = staffId,
            StaffName = "Staff Test"
        }, CancellationToken.None);

        _outboxWriter.Verify(x => x.WriteAsync(
            It.IsAny<TicketEscalatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private TicketEscalateRequestCommandHandler BuildHandler(
        Ticket ticket,
        TicketAssignment assignment,
        Guid staffId)
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            assignmentSeed: new[] { assignment });

        _stateMachine
            .Setup(x => x.CanTransition(ticket, TicketStatusEnum.Request, ActorRoleEnum.Staff, staffId))
            .Returns(new TransitionResult { IsAllowed = true });
        _stateMachine
            .Setup(x => x.ExecuteAsync(ticket, TicketStatusEnum.Request, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>((t, s, _, _) => t.Status = s)
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        _slaService
            .Setup(x => x.PauseSlaAsync(ticket.Id, It.IsAny<PauseReasonEnum>(), It.IsAny<string>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new TicketEscalateRequestCommandHandler(
            uow.Object,
            _stateMachine.Object,
            _logger.Object,
            _outboxWriter.Object,
            Mock.Of<MediatR.IPublisher>(),
            _slaService.Object);
    }
}
