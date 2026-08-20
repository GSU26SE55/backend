using FluentAssertions;
using MediatR;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class PeriodicMaintenanceAssignmentTests
{
    [Fact]
    public async Task ValidCustomerSchedule_ManagerMismatch_IsConflict()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(1));
        var handler = BuildHandler(ticket).handler;

        var result = await handler.Handle(Command(
            ticket,
            DateTimeOffset.UtcNow.AddDays(2)), CancellationToken.None);

        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("Customer-selected");
    }

    [Fact]
    public async Task ExpiredCustomerSchedule_WithoutContactNote_IsRejected()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddHours(-1));
        var handler = BuildHandler(ticket).handler;

        var result = await handler.Handle(Command(
            ticket,
            DateTimeOffset.UtcNow.AddDays(1)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Notes");
    }

    [Fact]
    public async Task ExpiredCustomerSchedule_WithContactNote_ReplacesAndAudits()
    {
        var oldSchedule = DateTime.UtcNow.AddHours(-1);
        var ticket = PeriodicTicket(oldSchedule);
        var staff = ActiveStaff();
        var setup = BuildHandler(ticket, staff);
        var newSchedule = DateTimeOffset.UtcNow.AddDays(1);
        var command = Command(ticket, newSchedule, staff.AccountId);
        command.Notes = "Called Customer and confirmed the replacement appointment.";

        var result = await setup.handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.ScheduledStartAtUtc.Should().BeCloseTo(newSchedule.UtcDateTime, TimeSpan.FromSeconds(1));
        ticket.Status.Should().Be(TicketStatusEnum.Pending);
        setup.outbox.Verify(x => x.WriteAsync(
            It.Is<PeriodicMaintenanceScheduleChangedEvent>(evt =>
                evt.PreviousScheduledStartAtUtc == oldSchedule &&
                evt.ChangedByRole == nameof(ActorRoleEnum.Manager) &&
                evt.Reason == command.Notes),
            It.IsAny<CancellationToken>()), Times.Once);
        setup.activity.Verify(x => x.LogAsync(
            ticket.Id,
            command.ManagerId,
            ActorRoleEnum.Manager,
            command.ManagerName,
            ActivityActionEnum.PeriodicMaintenanceScheduleChanged,
            oldSchedule.ToString("O"),
            It.IsAny<string>(),
            command.Notes), Times.Once);
    }

    private static (
        TicketAssignCommandHandler handler,
        Mock<IIntegrationEventOutboxWriter> outbox,
        Mock<IActivityLogger> activity) BuildHandler(
            Ticket ticket,
            StaffAccount? staff = null)
    {
        var staffSeed = staff is null ? Array.Empty<StaffAccount>() : [staff];
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket],
            staffSeed: staffSeed);
        Mock.Get(uow.Object.TicketAssignments)
            .Setup(x => x.AddAsync(It.IsAny<TicketAssignment>()))
            .Returns(Task.CompletedTask);
        Mock.Get(uow.Object.TicketParticipants)
            .Setup(x => x.AddAsync(It.IsAny<TicketParticipant>()))
            .Returns(Task.CompletedTask);

        var stateMachine = new Mock<ITicketStateMachine>();
        stateMachine.Setup(x => x.CanTransition(
                ticket,
                It.IsAny<TicketStatusEnum>(),
                ActorRoleEnum.Manager,
                It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = true });
        stateMachine.Setup(x => x.ExecuteAsync(
                ticket,
                It.IsAny<TicketStatusEnum>(),
                It.IsAny<TransitionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<Ticket, TicketStatusEnum, TransitionContext, CancellationToken>(
                (entity, status, _, _) => entity.Status = status)
            .ReturnsAsync(new TransitionResult { IsAllowed = true });

        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(x => x.WriteAsync(
                It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
        var publisher = new Mock<IPublisher>();
        publisher.Setup(x => x.Publish(
                It.IsAny<TicketAuditTrailNotification>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new TicketAssignCommandHandler(
            uow.Object,
            stateMachine.Object,
            activity.Object,
            outbox.Object,
            publisher.Object,
            Mock.Of<ITicketActivationService>());
        return (handler, outbox, activity);
    }

    private static TicketAssignCommand Command(
        Ticket ticket,
        DateTimeOffset schedule,
        Guid? staffId = null) => new()
        {
            TicketId = ticket.Id,
            PrimaryHandlerStaffId = staffId ?? Guid.NewGuid(),
            Priority = TicketPriorityEnum.P3Normal,
            ScheduledStartAt = schedule,
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        };

    private static Ticket PeriodicTicket(DateTime customerSchedule) => new()
    {
        Id = Guid.NewGuid(),
        Code = "TKT-PERIODIC",
        Title = "Periodic",
        Description = "Periodic",
        CustomerId = Guid.NewGuid(),
        Status = TicketStatusEnum.Open,
        ScheduledStartAtUtc = customerSchedule,
        PeriodicMaintenanceSourceTicketId = Guid.NewGuid(),
        PeriodicMaintenanceDueAtUtc = DateTime.UtcNow.AddDays(-1),
        PeriodicMaintenanceScheduleDeadlineAtUtc = DateTime.UtcNow.AddDays(7),
        PeriodicMaintenanceCustomerScheduledAtUtc = DateTime.UtcNow.AddDays(-2)
    };

    private static StaffAccount ActiveStaff() => new()
    {
        Id = Guid.NewGuid(),
        AccountId = Guid.NewGuid(),
        Email = "staff@example.com",
        FullName = "Staff",
        Status = AccountStatusEnum.Active,
        IsAvailable = true,
        SkillTier = StaffSkillTierEnum.SeniorSpecialist
    };
}
