using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class CustomerSchedulePeriodicMaintenanceCommandHandlerTests
{
    [Fact]
    public async Task Handle_OwningCustomerWithinWindow_PersistsAndWritesEvent()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(5));
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(x => x.WriteAsync(
                It.IsAny<PeriodicMaintenanceScheduleChangedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var activity = new Mock<IActivityLogger>();
        activity.Setup(x => x.LogAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ActorRoleEnum>(),
                It.IsAny<string?>(), It.IsAny<ActivityActionEnum>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var scheduledAt = DateTimeOffset.UtcNow.AddDays(1);
        var handler = new CustomerSchedulePeriodicMaintenanceCommandHandler(
            uow.Object,
            outbox.Object,
            activity.Object);

        var result = await handler.Handle(new CustomerSchedulePeriodicMaintenanceCommand
        {
            TicketId = ticket.Id,
            CustomerId = ticket.CustomerId,
            ScheduledStartAt = scheduledAt
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(scheduledAt.UtcDateTime, TimeSpan.FromSeconds(1));
        ticket.PeriodicMaintenanceCustomerScheduledAtUtc.Should().NotBeNull();
        ticket.ScheduleVersion.Should().Be(1);
        outbox.Verify(x => x.WriteAsync(
            It.Is<PeriodicMaintenanceScheduleChangedEvent>(evt =>
                evt.TicketId == ticket.Id &&
                evt.ChangedByRole == nameof(ActorRoleEnum.Customer) &&
                evt.ScheduleVersion == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherCustomer_IsForbidden()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(5));
        var handler = BuildHandler(ticket);

        var result = await handler.Handle(new CustomerSchedulePeriodicMaintenanceCommand
        {
            TicketId = ticket.Id,
            CustomerId = Guid.NewGuid(),
            ScheduledStartAt = DateTimeOffset.UtcNow.AddDays(1)
        }, CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_ExpiredOverdueWindow_IsConflict()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddMinutes(-1));
        ticket.PeriodicMaintenanceDueAtUtc = DateTime.UtcNow.AddDays(-2);
        var handler = BuildHandler(ticket);

        var result = await handler.Handle(new CustomerSchedulePeriodicMaintenanceCommand
        {
            TicketId = ticket.Id,
            CustomerId = ticket.CustomerId,
            ScheduledStartAt = DateTimeOffset.UtcNow.AddHours(1)
        }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_FutureTimeToday_IsAllowed()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(5));
        var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var result = await BuildHandler(ticket).Handle(Command(ticket, scheduledAt), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(scheduledAt.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_PastSchedule_IsBadRequest()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(5));

        var result = await BuildHandler(ticket).Handle(
            Command(ticket, DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("past");
    }

    [Fact]
    public async Task Handle_ScheduleAfterDeadline_IsBadRequest()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(1));

        var result = await BuildHandler(ticket).Handle(
            Command(ticket, DateTimeOffset.UtcNow.AddDays(2)),
            CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("deadline");
    }

    [Fact]
    public async Task Handle_NonPeriodicTicket_IsConflict()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(5));
        ticket.PeriodicMaintenanceSourceTicketId = null;

        var result = await BuildHandler(ticket).Handle(
            Command(ticket, DateTimeOffset.UtcNow.AddMinutes(10)),
            CancellationToken.None);

        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("periodic-maintenance");
    }

    [Fact]
    public async Task Handle_OverdueTicketWithinCatchUpWindow_IsAllowed()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(6));
        ticket.PeriodicMaintenanceDueAtUtc = DateTime.UtcNow.AddDays(-2);
        var scheduledAt = DateTimeOffset.UtcNow.AddDays(1);

        var result = await BuildHandler(ticket).Handle(Command(ticket, scheduledAt), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(scheduledAt.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    private static CustomerSchedulePeriodicMaintenanceCommand Command(
        Ticket ticket,
        DateTimeOffset scheduledAt) => new()
        {
            TicketId = ticket.Id,
            CustomerId = ticket.CustomerId,
            ScheduledStartAt = scheduledAt
        };

    private static CustomerSchedulePeriodicMaintenanceCommandHandler BuildHandler(Ticket ticket)
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        return new CustomerSchedulePeriodicMaintenanceCommandHandler(
            uow.Object,
            Mock.Of<IIntegrationEventOutboxWriter>(),
            Mock.Of<IActivityLogger>());
    }

    private static Ticket PeriodicTicket(DateTime deadlineAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        Code = "TKT-PERIODIC",
        Title = "Periodic",
        Description = "Periodic",
        CustomerId = Guid.NewGuid(),
        Status = TicketStatusEnum.Open,
        PeriodicMaintenanceSourceTicketId = Guid.NewGuid(),
        PeriodicMaintenanceDueAtUtc = DateTime.UtcNow.AddDays(5),
        PeriodicMaintenanceScheduleDeadlineAtUtc = deadlineAtUtc
    };
}
