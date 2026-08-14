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

public class TicketReopenCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();

    [Fact]
    public async Task Handle_ValidReopen_ReopensTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed,
            CustomerId = customerId,
            ApprovedAt = DateTime.UtcNow.AddDays(-3),
            ClosedAt = DateTime.UtcNow.AddDays(-3)
        };

        var command = new TicketReopenCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            CustomerName = "Customer A",
            ReopenReason = "The issue has still not been fully resolved."
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketReopenCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(TicketStatusEnum.Open);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Open, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketReopenedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SecondReopen_AutoEscalates()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed,
            CustomerId = customerId,
            ApprovedAt = DateTime.UtcNow.AddDays(-1),
            ClosedAt = DateTime.UtcNow.AddDays(-1),
            ReopenCount = 1 // Already reopened once
        };

        // Mock ExecuteAsync for Open transition to increment ReopenCount
        _stateMachine.Setup(x => x.ExecuteAsync(ticket, TicketStatusEnum.Open, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>((t, target, ctx, ct) =>
            {
                t.Status = target;
                t.ReopenCount++;
            })
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        var command = new TicketReopenCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            ReopenReason = "Still faulty."
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketReopenCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.ReopenCount.Should().Be(2);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Open, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketReopenedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReopenAfter7Days_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed,
            CustomerId = customerId,
            ApprovedAt = DateTime.UtcNow.AddDays(-8),
            ClosedAt = DateTime.UtcNow.AddDays(-8)
        };

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, customerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Only Customer can reopen within 7 days of approval." });

        var command = new TicketReopenCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            ReopenReason = "It has taken far too long."
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketReopenCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("seven-day");
    }
}
