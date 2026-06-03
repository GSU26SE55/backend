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

public class TicketResolveCommandHandlerTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();

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
            AssignedStaffId = staffId
        };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            StaffName = "Staff A",
            ResolutionSummary = "Fixed"
        };

        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(ticketId.ToString());
        result.Data.Status.Should().Be(TicketStatusEnum.Resolved);

        _stateMachine.Verify(x => x.ExecuteAsync(ticket, TicketStatusEnum.Resolved, It.IsAny<TransitionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.OutboxMessages.AddAsync(It.IsAny<OutboxMessage>()), Times.Once);
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
            AssignedStaffId = escalatedStaffId,
            EscalatedAt = DateTime.UtcNow
        };

        var command = new TicketResolveCommand
        {
            TicketId = ticketId,
            StaffId = originalStaffId
        };

        var (uow, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

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
            AssignedStaffId = staffId,
            EscalationReason = EscalationReasonEnum.SkillGap
        };

        var staff = new StaffAccount { AccountId = staffId, SkillTier = StaffSkillTierEnum.Generalist };

        var command = new TicketResolveCommand { TicketId = ticketId, StaffId = staffId };

        var (uow, _, _, _, staffRepo) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket },
            staffSeed: new[] { staff });

        var handler = new TicketResolveCommandHandler(uow.Object, _stateMachine.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Contain("Cần Staff Tier 2 trở lên");
    }
}
