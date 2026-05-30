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

public class TicketAssignCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();

    #region Happy Path
    [Fact]
    public async Task Handle_ApprovedTicket_AssignsStaffSuccessfully()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Status = TicketStatusEnum.Approved,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var command = new TicketAssignCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            ManagerId = managerId,
            ManagerName = "Manager A",
            Notes = "Please handle this."
        };

        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.AssignedStaffId.Should().Be(staffId);
        ticket.Status.Should().Be(TicketStatusEnum.Assigned);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Assigned, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(ticketId, managerId, ActorRoleEnum.Manager, "Manager A", ActivityActionEnum.StaffAssigned, null, staffId.ToString(), "Please handle this."), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    #endregion

    #region Failure Cases
    [Fact]
    public async Task Handle_OpenTicketNotApproved_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Open, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" }; // Not Approved yet

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = managerId };

        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Ticket must be Approved before assignment." });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Ticket must be Approved before assignment.");
    }

    [Fact]
    public async Task Handle_NonManager_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid(); // Trying to assign themselves
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Approved, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = staffId };

        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, staffId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Only Managers can assign staff." });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
    #endregion
}
