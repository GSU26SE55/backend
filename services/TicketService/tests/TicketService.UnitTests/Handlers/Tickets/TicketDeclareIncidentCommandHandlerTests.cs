using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketDeclareIncidentCommandHandlerTests
{
    [Fact]
    public async Task Handle_P1Ticket_PromotesToUrgentAndCreatesIncidentEpisode()
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-1",
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P1Critical,
            Title = "Test ticket",
            Description = "Test description"
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var batteryAssets = new Mock<IGenericRepository<TicketBatteryAsset>>();
        batteryAssets.Setup(x => x.GetAllAsync()).Returns(Array.Empty<TicketBatteryAsset>().BuildMock());
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(batteryAssets.Object);
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var logger = new Mock<IActivityLogger>();
        var handler = new TicketDeclareIncidentCommandHandler(
            uow.Object, outbox.Object, logger.Object, Mock.Of<TicketService.Application.Interfaces.Services.ITicketActivationService>());

        var result = await handler.Handle(new TicketDeclareIncidentCommand
        {
            TicketId = ticket.Id,
            UserId = Guid.NewGuid(),
            UserDisplayName = "Manager",
            IncidentDescription = "Safety issue"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.Urgent);
        ticket.Status.Should().Be(TicketStatusEnum.ReAssign);
        ticket.ActiveIncidentEpisodeId.Should().NotBeNull();
        outbox.Verify(x => x.WriteAsync(
            It.Is<BatteryIsolationRequestedEvent>(e =>
                e.IncidentEpisodeId == ticket.ActiveIncidentEpisodeId &&
                e.Id == DeterministicEventId.From(ticket.ActiveIncidentEpisodeId!.Value, "battery-isolation-requested")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MissingTicket_ReturnsNotFound()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: Array.Empty<Ticket>());
        var handler = new TicketDeclareIncidentCommandHandler(
            uow.Object,
            Mock.Of<IIntegrationEventOutboxWriter>(),
            Mock.Of<IActivityLogger>(),
            Mock.Of<TicketService.Application.Interfaces.Services.ITicketActivationService>());

        var result = await handler.Handle(new TicketDeclareIncidentCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            IncidentDescription = "Safety issue"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ExistingIncidentEpisode_ReturnsSuccessfulExistingStateWithoutAnotherEvent()
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-EXISTING",
            Status = TicketStatusEnum.ReAssign,
            Priority = TicketPriorityEnum.Urgent,
            IsIncident = true,
            ActiveIncidentEpisodeId = Guid.NewGuid(),
            Title = "Existing incident",
            Description = "Existing incident"
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var handler = new TicketDeclareIncidentCommandHandler(
            uow.Object, outbox.Object, Mock.Of<IActivityLogger>(),
            Mock.Of<TicketService.Application.Interfaces.Services.ITicketActivationService>());

        var result = await handler.Handle(new TicketDeclareIncidentCommand
        {
            TicketId = ticket.Id,
            UserId = Guid.NewGuid(),
            IncidentDescription = "Repeated request"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Status.Should().Be(TicketStatusEnum.ReAssign);
        outbox.Verify(x => x.WriteAsync(It.IsAny<BatteryIsolationRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConcurrencyLoser_ReloadsAndReturnsWinningIncidentState()
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-RACE",
            Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P1Critical,
            Title = "Concurrent incident",
            Description = "Concurrent incident"
        };
        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var winningEpisodeId = Guid.NewGuid();
        uow.Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Incident race"));
        tickets.Setup(x => x.ReloadAsync(ticket, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                ticket.Status = TicketStatusEnum.ReAssign;
                ticket.Priority = TicketPriorityEnum.Urgent;
                ticket.IsIncident = true;
                ticket.ActiveIncidentEpisodeId = winningEpisodeId;
            })
            .Returns(Task.CompletedTask);
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        var handler = new TicketDeclareIncidentCommandHandler(
            uow.Object, outbox.Object, Mock.Of<IActivityLogger>(),
            Mock.Of<TicketService.Application.Interfaces.Services.ITicketActivationService>());

        var result = await handler.Handle(new TicketDeclareIncidentCommand
        {
            TicketId = ticket.Id,
            UserId = Guid.NewGuid(),
            IncidentDescription = "Concurrent request"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        ticket.ActiveIncidentEpisodeId.Should().Be(winningEpisodeId);
        tickets.Verify(x => x.ReloadAsync(ticket, It.IsAny<CancellationToken>()), Times.Once);
        outbox.Verify(x => x.WriteAsync(It.IsAny<BatteryIsolationRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
