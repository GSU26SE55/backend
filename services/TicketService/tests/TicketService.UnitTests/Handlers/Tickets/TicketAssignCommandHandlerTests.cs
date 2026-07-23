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

public class TicketAssignCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IMessageProducerService> _producer = new();
    // Sprint Bonus NS-12 (#656) — dùng SlaCalculator thật (pure util) để assert DueAt theo giờ SLA.
    private readonly ISlaCalculator _slaCalculator = new TicketService.Infrastructure.Implements.Utils.SlaCalculator();

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

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true }
        };

        var command = new TicketAssignCommand
        {
            TicketId = ticketId,
            PrimaryHandlerStaffId = staffId,
            ManagerId = managerId,
            ManagerName = "Manager A",
            Notes = "Please handle this."
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        ticket.PrimaryHandlerStaffId.Should().Be(staffId);
        ticket.Status.Should().Be(TicketStatusEnum.Assigned);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Assigned, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(ticketId, managerId, ActorRoleEnum.Manager, "Manager A", ActivityActionEnum.StaffAssigned, null, staffId.ToString(), "Please handle this."), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApprovedTicket_CreatesPrimaryAssigneeParticipant()
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

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true }
        };

        var command = new TicketAssignCommand { TicketId = ticketId, PrimaryHandlerStaffId = staffId, ManagerId = managerId, ManagerName = "Manager A" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, staffSeed: staff);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        participants.Verify(x => x.AddAsync(It.Is<TicketParticipant>(p =>
            p.UserId == staffId && p.ParticipantType == ParticipantTypeEnum.PrimaryAssignee)), Times.Once);
    }
    #endregion

    #region Failure Cases
    [Fact]
    public async Task Handle_OpenTicketNotApproved_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Open, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" }; // Not Approved yet

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true }
        };

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = managerId, PrimaryHandlerStaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Ticket must be Approved before assignment." });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

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
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Approved, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true }
        };

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = managerId, PrimaryHandlerStaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        _stateMachine.Setup(x => x.CanTransition(ticket, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, managerId))
            .Returns(new TransitionResult { IsAllowed = false, Reason = "Only Managers can assign staff." });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_P2HighTicket_AssignedToGeneralist_Returns403()
    {
        // Arrange — tier validation: P2High requires ModuleSpecialist+; Generalist is rejected
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Status = TicketStatusEnum.Approved,
            Priority = TicketPriorityEnum.P2High,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true, SkillTier = StaffSkillTierEnum.Generalist }
        };

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = managerId, PrimaryHandlerStaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Contain("ModuleSpecialist");
    }

    [Fact]
    public async Task Handle_NoPriorityTicket_GeneralistAssigned_Succeeds()
    {
        // Arrange — no Priority set → tier check skipped → assignment succeeds regardless of category
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Status = TicketStatusEnum.Approved,
            Category = TicketCategoryEnum.Overheat,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var staff = new List<StaffAccount>
        {
            new StaffAccount { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true, SkillTier = StaffSkillTierEnum.Generalist }
        };

        var command = new TicketAssignCommand { TicketId = ticketId, ManagerId = managerId, PrimaryHandlerStaffId = staffId };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("Ticket assigned successfully.");
    }
    #endregion

    #region Sprint Bonus NS-12 (#656, R1) — SlaTimer khi Assigned
    [Fact]
    public async Task Handle_TicketWithPriority_CreatesRunningSlaTimer_DueAtByPriority()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Status = TicketStatusEnum.Approved,
            Priority = TicketPriorityEnum.P3Normal,
            Code = "TKT-001",
            Title = "T",
            Description = "D"
        };
        var staff = new List<StaffAccount> { new() { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var command = new TicketAssignCommand { TicketId = ticketId, PrimaryHandlerStaffId = staffId, ManagerId = Guid.NewGuid(), ManagerName = "Mgr" };

        var (uow, _, _, _, _, slaTimers, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);
        SlaTimer? captured = null;
        slaTimers.Setup(r => r.AddAsync(It.IsAny<SlaTimer>()))
            .Callback<SlaTimer>(t => captured = t).Returns(Task.CompletedTask);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);
        await handler.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull("ticket có Priority → timer phải được tạo");
        captured!.Status.Should().Be(SlaTimerStatusEnum.Running);
        captured.Priority.Should().Be(TicketPriorityEnum.P3Normal);
        captured.TicketId.Should().Be(ticketId);
        (captured.DueAt - captured.StartedAt).Should().Be(TimeSpan.FromHours(72), "P3 = SLA 72h");
        captured.OriginalDueAt.Should().Be(captured.DueAt);
    }

    [Fact]
    public async Task Handle_TicketAlreadyHasTimer_DoesNotCreateSecond()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Status = TicketStatusEnum.Approved,
            Priority = TicketPriorityEnum.P2High,
            Code = "TKT-002",
            Title = "T",
            Description = "D"
        };
        var staff = new List<StaffAccount> { new() { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var existingTimer = new SlaTimer { Id = Guid.NewGuid(), TicketId = ticketId, Status = SlaTimerStatusEnum.Running, Priority = TicketPriorityEnum.P2High, StartedAt = DateTime.UtcNow.AddHours(-1), DueAt = DateTime.UtcNow.AddHours(23) };
        var command = new TicketAssignCommand { TicketId = ticketId, PrimaryHandlerStaffId = staffId, ManagerId = Guid.NewGuid(), ManagerName = "Mgr" };

        var (uow, _, _, _, _, slaTimers, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff, slaTimerSeed: new[] { existingTimer });

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);
        await handler.Handle(command, CancellationToken.None);

        slaTimers.Verify(r => r.AddAsync(It.IsAny<SlaTimer>()), Times.Never, "reassign không tạo timer thứ 2");
    }

    [Fact]
    public async Task Handle_TicketWithoutPriority_DoesNotCreateTimer()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Status = TicketStatusEnum.Approved, Code = "TKT-003", Title = "T", Description = "D" }; // Priority null
        var staff = new List<StaffAccount> { new() { AccountId = staffId, Status = AccountStatusEnum.Active, IsAvailable = true } };
        var command = new TicketAssignCommand { TicketId = ticketId, PrimaryHandlerStaffId = staffId, ManagerId = Guid.NewGuid(), ManagerName = "Mgr" };

        var (uow, _, _, _, _, slaTimers, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, staffSeed: staff);

        var handler = new TicketAssignCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>(), _slaCalculator);
        await handler.Handle(command, CancellationToken.None);

        slaTimers.Verify(r => r.AddAsync(It.IsAny<SlaTimer>()), Times.Never);
    }
    #endregion
}
