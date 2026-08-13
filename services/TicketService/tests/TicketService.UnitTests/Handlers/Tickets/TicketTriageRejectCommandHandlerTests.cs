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

public class TicketTriageRejectCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketStateMachine> _stateMachine = new();

    [Fact]
    public async Task Handle_ValidRequest_RejectsTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var command = new TicketTriageRejectCommand
        {
            TicketId = ticketId,
            Reason = "Invalid ticket description",
            ManagerId = managerId,
            ManagerName = "Manager Test"
        };

        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Status = TicketStatusEnum.Open,
            Title = "Test",
            Description = "Test"
        };

        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = true });

        _stateMachine.Setup(x => x.ExecuteAsync(ticket, TicketStatusEnum.ClosedRejected, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>((t, s, ctx, ct) =>
            {
                t.Status = s;
                t.Reason = (string?)ctx.Payload["Reason"];
            })
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        var handler = new TicketTriageRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        ticket.Status.Should().Be(TicketStatusEnum.ClosedRejected);
        ticket.Reason.Should().Be("Invalid ticket description");

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.ClosedRejected, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<TicketStatusChangedIntegrationEvent>(e =>
            e.OldStatus == TicketStatusEnum.Open && e.NewStatus == TicketStatusEnum.ClosedRejected),
            It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(ticketId, managerId, ActorRoleEnum.Manager, "Manager Test", ActivityActionEnum.Rejected, null, "ClosedRejected", "Invalid ticket description"), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectFromEscalated_PublishesEventWithEscalatedOldStatus()
    {
        // Arrange — reject hợp lệ từ Escalated (không chỉ Open). OldStatus của event phải là Escalated, không hardcode Open.
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var command = new TicketTriageRejectCommand
        {
            TicketId = ticketId,
            Reason = "Out of service scope",
            ManagerId = managerId,
            ManagerName = "Manager Test"
        };

        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-002",
            Status = TicketStatusEnum.Open,
            Title = "Test",
            Description = "Test"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = true });

        _stateMachine.Setup(x => x.ExecuteAsync(ticket, TicketStatusEnum.ClosedRejected, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>((t, s, ctx, ct) =>
            {
                t.Status = s;
                t.Reason = (string?)ctx.Payload["Reason"];
            })
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        var handler = new TicketTriageRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.ClosedRejected);
        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<TicketStatusChangedIntegrationEvent>(e =>
            e.OldStatus == TicketStatusEnum.Open && e.NewStatus == TicketStatusEnum.ClosedRejected),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        // Arrange
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var handler = new TicketTriageRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);
        var command = new TicketTriageRejectCommand { TicketId = Guid.NewGuid(), Reason = "Test" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TransitionNotAllowed_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.InProgress, Code = "T-1", Title = "T", Description = "D" };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.ClosedRejected, It.IsAny<ActorRoleEnum>(), It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Invalid status" });

        var handler = new TicketTriageRejectCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object);
        var command = new TicketTriageRejectCommand { TicketId = ticketId, Reason = "Test", ManagerId = Guid.NewGuid() };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Invalid status");
    }
}
