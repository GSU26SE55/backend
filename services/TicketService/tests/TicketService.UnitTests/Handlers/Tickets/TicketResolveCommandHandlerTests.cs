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

public class TicketResolveCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task Handle_ValidRequest_ResolvesTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            Status = TicketStatusEnum.InProgress,
            PrimaryHandlerStaffId = staffId
        };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            StaffName = "Staff A",
            ResolutionSummary = "Fixed"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Status.Should().Be(TicketStatusEnum.Resolved);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Resolved, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketResolvedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EscalatedTicket_ResolveByOriginalStaff_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var originalStaffId = Guid.NewGuid();
        var escalatedStaffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            PrimaryHandlerStaffId = escalatedStaffId,
            EscalatedAt = DateTime.UtcNow
        };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = originalStaffId
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Contain("Chỉ Staff đang được assign");
    }

    [Fact]
    public async Task Handle_SkillGapEscalation_LowTierStaff_Returns403()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            PrimaryHandlerStaffId = staffId,
            EscalationReason = EscalationReasonEnum.SkillGap
        };

        var staff = new StaffAccount { AccountId = staffId, SkillTier = StaffSkillTierEnum.Generalist };

        var command = new TicketResolveCommand { TicketId = ticketId, StaffId = staffId, ResolutionSummary = "Fixed" };

        var (uow, _, _, _, staffRepo, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Contain("Cần Staff Tier cao hơn hiện tại cho SkillGap escalation.");
    }

    [Fact]
    public async Task Handle_EscalatedTicket_ResolveByCorrectStaff_ResolvesTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            Status = TicketStatusEnum.InProgress,
            PrimaryHandlerStaffId = staffId,
            EscalatedAt = DateTime.UtcNow
        };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            StaffName = "Senior Staff",
            ResolutionSummary = "Escalation resolved"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _logger.Verify(x => x.LogAsync(ticketId, staffId, ActorRoleEnum.Staff, "Senior Staff", ActivityActionEnum.ResolvedByEscalatedStaff, null, "Escalation resolved", null), Times.Once);
    }

    [Fact]
    public async Task Handle_SkillGapEscalation_HighTierStaff_ResolvesTicket()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Desc",
            Status = TicketStatusEnum.InProgress,
            PrimaryHandlerStaffId = staffId,
            EscalationReason = EscalationReasonEnum.SkillGap,
            EscalatedAt = DateTime.UtcNow
        };

        var staff = new StaffAccount { AccountId = staffId, SkillTier = StaffSkillTierEnum.ModuleSpecialist };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            StaffName = "Specialist Staff",
            ResolutionSummary = "Expertly fixed"
        };

        var (uow, _, _, _, staffRepo, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
