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

public class TicketRateCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();

    [Fact]
    public async Task Handle_ValidRate_ClosesTicket()
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
            ClosedAt = DateTime.UtcNow.AddDays(-1)
        };

        var command = new TicketRateCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            CustomerName = "Customer A",
            Rating = 5,
            RatingComment = "Very satisfied!"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketRateCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(TicketStatusEnum.Closed);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, It.IsAny<TicketStatusEnum>(), It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketRatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RateNonResolvedTicket_Returns403()
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
            Status = TicketStatusEnum.InProgress,
            CustomerId = customerId
        };

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.Customer, customerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Cannot transition from InProgress to Closed." });

        var command = new TicketRateCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            Rating = 4
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketRateCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
