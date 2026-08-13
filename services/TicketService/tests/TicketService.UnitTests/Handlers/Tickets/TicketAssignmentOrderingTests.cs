using MediatR;
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

public sealed class TicketAssignmentOrderingTests
{
    [Fact]
    public async Task ImmediateAssignment_ReopenedIncidentWithNormalPriority_RetiresActiveEpisode()
    {
        var previousEpisodeId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-DECLASSIFY",
            CustomerId = Guid.NewGuid(),
            Title = "Reopened incident",
            Description = "Reopened incident",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.ManualByCustomer,
            Priority = TicketPriorityEnum.Urgent,
            ReopenCount = 1,
            IsIncident = true,
            ActiveIncidentEpisodeId = previousEpisodeId
        };
        var staff = new StaffAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Email = "staff@example.com",
            FullName = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            SkillTier = StaffSkillTierEnum.SeniorSpecialist
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: [ticket], staffSeed: [staff]);
        var stateMachine = new Mock<ITicketStateMachine>();
        stateMachine.Setup(x => x.CanTransition(
                ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Manager, It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = true });
        var activation = new Mock<ITicketActivationService>();
        activation.Setup(x => x.ActivateAsync(It.IsAny<ActivationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivationResult(true));
        var logger = new Mock<IActivityLogger>();
        logger.Setup(x => x.LogAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string?>(),
                It.IsAny<ActivityActionEnum>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var publisher = new Mock<IPublisher>();
        publisher.Setup(x => x.Publish(It.IsAny<TicketAuditTrailNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new TicketAssignCommandHandler(
            uow.Object,
            stateMachine.Object,
            logger.Object,
            Mock.Of<IIntegrationEventOutboxWriter>(),
            publisher.Object,
            activation.Object);

        var result = await handler.Handle(new TicketAssignCommand
        {
            TicketId = ticket.Id,
            PrimaryHandlerStaffId = staff.AccountId,
            Priority = TicketPriorityEnum.P1Critical,
            ScheduledStartAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager",
            Notes = "Reviewed after reopen"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.IsIncident.Should().BeFalse();
        ticket.ActiveIncidentEpisodeId.Should().BeNull();
        logger.Verify(x => x.LogAsync(
            ticket.Id,
            It.IsAny<Guid>(),
            ActorRoleEnum.Manager,
            "Manager",
            ActivityActionEnum.IncidentDeclassified,
            previousEpisodeId.ToString(),
            TicketPriorityEnum.P1Critical.ToString(),
            "Reviewed after reopen"), Times.Once);
    }

    [Fact]
    public async Task ImmediateAssignment_WritesAssignedEventBeforeActivation()
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-ORDER",
            CustomerId = Guid.NewGuid(),
            Title = "Test",
            Description = "Test",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var staff = new StaffAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Email = "staff@example.com",
            FullName = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            SkillTier = StaffSkillTierEnum.SeniorSpecialist
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff });

        var calls = new List<string>();
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(x => x.WriteAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("assigned"))
            .Returns(Task.CompletedTask);

        var activation = new Mock<ITicketActivationService>();
        activation.Setup(x => x.ActivateAsync(It.IsAny<ActivationRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("activation"))
            .ReturnsAsync(new ActivationResult(true, null));

        var stateMachine = new Mock<ITicketStateMachine>();
        stateMachine.Setup(x => x.CanTransition(
                ticket,
                TicketStatusEnum.InProgress,
                ActorRoleEnum.Manager,
                It.IsAny<Guid>()))
            .Returns(new TransitionResult { IsAllowed = true });

        var activity = new Mock<IActivityLogger>();
        activity.Setup(x => x.LogAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string?>(),
                It.IsAny<ActivityActionEnum>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IPublisher>();
        publisher.Setup(x => x.Publish(It.IsAny<TicketAuditTrailNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new TicketAssignCommandHandler(
            uow.Object,
            stateMachine.Object,
            activity.Object,
            outbox.Object,
            publisher.Object,
            activation.Object);

        var result = await handler.Handle(new TicketAssignCommand
        {
            TicketId = ticket.Id,
            PrimaryHandlerStaffId = staff.AccountId,
            Priority = TicketPriorityEnum.P1Critical,
            ScheduledStartAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        calls.Should().Equal("assigned", "activation");
    }
}
