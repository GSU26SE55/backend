using FluentAssertions;
using Moq;
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

/// <summary>
/// Sprint 6.2 — phía PHÁT event của TicketService.
/// NOTI-05 (#676): ticket event mang thêm CustomerId/Priority, SLA warning mang StaffId.
/// NOTI-07 (#678): các state cuối vòng đời publish thêm event SharedContracts để
/// NotificationService (assembly khác) consume được — event nội bộ cũ vẫn giữ nguyên.
/// </summary>
public class Sprint62PublisherTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly ISlaCalculator _slaCalculator = new TicketService.Infrastructure.Implements.Utils.SlaCalculator();

    private static Ticket MakeTicket(TicketStatusEnum status, Guid customerId, Guid? staffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "TKT-620",
        Title = "T",
        Description = "D",
        Status = status,
        CustomerId = customerId,
        PrimaryHandlerStaffId = staffId,
        Priority = TicketPriorityEnum.P2High,
    };

    // ── NOTI-05 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_PublishesTicketAssignedEvent_WithCustomerId()
    {
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.Open, customerId);

        var staff = new List<StaffAccount>
        {
            new() { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true, SkillTier = StaffSkillTierEnum.SeniorSpecialist }
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket], staffSeed: staff);
        var handler = new TicketAssignCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object,
            Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        await handler.Handle(new TicketAssignCommand
        {
            TicketId = ticket.Id,
            PrimaryHandlerStaffId = staffId,
            ManagerId = Guid.NewGuid(),
            ManagerName = "M"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketAssignedEvent>(e => e.CustomerId == customerId && e.PrimaryHandlerStaffId == staffId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_PublishesTicketResolvedEvent_WithCustomerId()
    {
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.InProgress, customerId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketResolveCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object, Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new TicketResolveCommand
        {
            TicketId = ticket.Id,
            StaffId = staffId,
            StaffName = "S",
            ResolutionSummary = "Đã thay cell"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketResolvedEvent>(e => e.CustomerId == customerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── NOTI-07 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_PublishesSharedTicketApprovedEvent()
    {
        var customerId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.Resolved, customerId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketApproveCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object);

        await handler.Handle(new TicketApproveCommand
        {
            TicketId = ticket.Id,
            ManagerId = managerId,
            ManagerName = "M",
            ManagerComment = "OK"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketApprovedEvent>(e =>
                e.CustomerId == customerId && e.ManagerId == managerId && e.ManagerComment == "OK"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_PublishesSharedTicketRejectedEvent_NotClosedRejected()
    {
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.Resolved, customerId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketRejectCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object, Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new TicketRejectCommand
        {
            TicketId = ticket.Id,
            ManagerId = Guid.NewGuid(),
            ManagerName = "M",
            Reason = "Chưa đạt"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketRejectedEvent>(e =>
                !e.IsClosedRejected && e.StaffId == staffId && e.Reason == "Chưa đạt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TriageReject_PublishesSharedTicketRejectedEvent_ClosedRejected()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.New, customerId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketTriageRejectCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object);

        await handler.Handle(new TicketTriageRejectCommand
        {
            TicketId = ticket.Id,
            ManagerId = Guid.NewGuid(),
            ManagerName = "M",
            Reason = "Ngoài scope"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketRejectedEvent>(e => e.IsClosedRejected && e.CustomerId == customerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rate_PublishesSharedTicketClosedEvent_WithRating()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.ClosedPendingRate, customerId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketRateCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object, Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new TicketRateCommand
        {
            TicketId = ticket.Id,
            CustomerId = customerId,
            CustomerName = "C",
            Rating = 5,
            RatingComment = "Tốt"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketClosedEvent>(e => e.Rating == 5 && !e.IsAutoClosed && e.CustomerId == customerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reopen_PublishesSharedTicketReopenedEvent()
    {
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.ClosedPendingRate, customerId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketReopenCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object, Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new TicketReopenCommand
        {
            TicketId = ticket.Id,
            CustomerId = customerId,
            CustomerName = "C",
            ReopenReason = "Vẫn lỗi"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketReopenedEvent>(e => e.CustomerId == customerId && e.StaffId == staffId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Start_PublishesSharedTicketStatusChangedEvent()
    {
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(TicketStatusEnum.Assigned, customerId, staffId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = new TicketStartCommandHandler(
            uow.Object, _stateMachine.Object, _activityLogger.Object, _outboxWriter.Object, Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new TicketStartCommand
        {
            TicketId = ticket.Id,
            StaffId = staffId,
            StaffName = "S"
        }, CancellationToken.None);

        _outboxWriter.Verify(p => p.WriteAsync(
            It.Is<TicketStatusChangedEvent>(e =>
                e.CustomerId == customerId &&
                e.NewStatusName == nameof(TicketStatusEnum.InProgress)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
