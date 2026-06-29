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

public class TicketTriageCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IPriorityCalculator> _priorityCalc = new();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    #region Happy Path
    [Fact]
    public async Task Handle_ValidRequest_CalculatesPriorityAndApproves()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Status = TicketStatusEnum.Open,
            Title = "Test",
            Description = "Test Description"
        };

        var command = new TicketTriageCommand
        {
            TicketId = ticketId,
            ManagerId = managerId,
            ManagerName = "Manager A",
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            ManagerComment = "Urgent site issue"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        _priorityCalc.Setup(x => x.Calculate(ImpactScopeEnum.Site, UrgencyLevelEnum.High))
            .Returns(TicketPriorityEnum.P1Critical);

        var handler = new TicketTriageCommandHandler(uow.Object, _stateMachine.Object, _priorityCalc.Object, _logger.Object, _producer.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Approved);
        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        ticket.ImpactScope.Should().Be(ImpactScopeEnum.Site);
        ticket.UrgencyLevel.Should().Be(UrgencyLevelEnum.High);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Approved, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(ticketId, managerId, ActorRoleEnum.Manager, "Manager A", ActivityActionEnum.TriageApproved, It.IsAny<string>(), It.IsAny<string>(), "Urgent site issue"), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketStatusChangedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithManualPriority_OverridesCalculation()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Open, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };

        var command = new TicketTriageCommand
        {
            TicketId = ticketId,
            ManagerId = managerId,
            Impact = ImpactScopeEnum.SingleAsset,
            Urgency = UrgencyLevelEnum.Low,
            ManualPriority = TicketPriorityEnum.P1Critical, // Override
            PriorityOverrideReason = "CEO Request"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketTriageCommandHandler(uow.Object, _stateMachine.Object, _priorityCalc.Object, _logger.Object, _producer.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _priorityCalc.Verify(x => x.Calculate(It.IsAny<ImpactScopeEnum>(), It.IsAny<UrgencyLevelEnum>()), Times.Never);
        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketStatusChangedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    #endregion

    #region Failure Cases
    [Fact]
    public async Task Handle_InvalidState_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Closed, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var command = new TicketTriageCommand { TicketId = ticketId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Approved, It.IsAny<ActorRoleEnum>(), It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Ticket is closed" });

        var handler = new TicketTriageCommandHandler(uow.Object, _stateMachine.Object, _priorityCalc.Object, _logger.Object, _producer.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Ticket is closed");
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var command = new TicketTriageCommand { TicketId = Guid.NewGuid() };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: Enumerable.Empty<Ticket>());

        var handler = new TicketTriageCommandHandler(uow.Object, _stateMachine.Object, _priorityCalc.Object, _logger.Object, _producer.Object);
        var result = await handler.Handle(command, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }
    #endregion
}
