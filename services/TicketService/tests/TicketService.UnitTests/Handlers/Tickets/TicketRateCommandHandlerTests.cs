using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketRateCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();

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
            Status = TicketStatusEnum.ClosedPendingRate,
            CustomerId = customerId
        };

        var command = new TicketRateCommand
        {
            TicketId = ticketId,
            CustomerId = customerId,
            CustomerName = "Customer A",
            Rating = 5,
            RatingComment = "Rất hài lòng!"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketRateCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(TicketStatusEnum.Closed);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Closed, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
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

        var handler = new TicketRateCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
