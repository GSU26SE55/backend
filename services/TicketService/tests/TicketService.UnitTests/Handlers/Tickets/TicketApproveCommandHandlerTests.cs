using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketApproveCommandHandlerTests
{
    [Fact]
    public async Task Handle_IncidentTicket_SnapshotsEpisodeIdInApprovalActivity()
    {
        var episodeId = Guid.NewGuid();
        var closedAt = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-INCIDENT-CLOSE",
            Title = "Incident close",
            Description = "Incident close",
            Status = TicketStatusEnum.Completed,
            IsIncident = true,
            ActiveIncidentEpisodeId = episodeId
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var stateMachine = new Mock<ITicketStateMachine>();
        stateMachine.Setup(x => x.CanTransition(
                ticket, TicketStatusEnum.Closed, ActorRoleEnum.Manager, It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = true });
        stateMachine.Setup(x => x.ExecuteAsync(
                ticket, TicketStatusEnum.Closed, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                ticket.Status = TicketStatusEnum.Closed;
                ticket.ApprovedAt = closedAt.AddMilliseconds(-1);
                ticket.ClosedAt = closedAt;
            })
            .ReturnsAsync(new TransitionResult { IsAllowed = true });
        var logger = new Mock<IActivityLogger>();
        var outboxWriter = new Mock<IIntegrationEventOutboxWriter>();
        var handler = new TicketApproveCommandHandler(
            uow.Object,
            stateMachine.Object,
            logger.Object,
            outboxWriter.Object);

        var result = await handler.Handle(new TicketApproveCommand
        {
            TicketId = ticket.Id,
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager",
            ManagerComment = "Approved after incident resolution."
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        logger.Verify(x => x.LogAsync(
            ticket.Id,
            It.IsAny<Guid>(),
            ActorRoleEnum.Manager,
            "Manager",
            ActivityActionEnum.Approved,
            episodeId.ToString(),
            null,
            "Approved after incident resolution."), Times.Once);
        outboxWriter.Verify(x => x.WriteAsync(
            It.Is<TicketClosedEvent>(e => e.ClosedAt == closedAt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(TicketStatusEnum.Open)]
    [InlineData(TicketStatusEnum.Pending)]
    [InlineData(TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.Request)]
    [InlineData(TicketStatusEnum.ReAssign)]
    public async Task Handle_NonCompletedTicket_ReturnsConflictWithoutTransition(TicketStatusEnum status)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-1176",
            Title = "Lifecycle guard",
            Description = "Lifecycle guard",
            Status = status
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var stateMachine = new Mock<ITicketStateMachine>();
        var sut = new TicketApproveCommandHandler(
            uow.Object,
            stateMachine.Object,
            Mock.Of<IActivityLogger>(),
            Mock.Of<IIntegrationEventOutboxWriter>());

        var result = await sut.Handle(new TicketApproveCommand
        {
            TicketId = ticket.Id,
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        stateMachine.Verify(x => x.CanTransition(
            It.IsAny<Ticket>(), It.IsAny<TicketStatusEnum>(), It.IsAny<ActorRoleEnum>(), It.IsAny<Guid>()), Times.Never);
    }
}
