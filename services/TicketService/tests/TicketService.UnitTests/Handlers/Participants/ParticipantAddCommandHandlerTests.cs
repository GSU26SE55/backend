using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantAddCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    private static Ticket MakeTicket(Guid ticketId) => new()
    {
        Id = ticketId,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description"
    };

    [Fact]
    public async Task Handle_ManagerAddsCollaborator_Returns201()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantAddCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.Collaborator,
            CanPost = true,
            CanViewInternal = true,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.UserId.Should().Be(targetUserId.ToString());

        participants.Verify(x => x.AddAsync(It.Is<TicketParticipant>(p =>
            p.TicketId == ticketId && p.UserId == targetUserId && p.ParticipantType == ParticipantTypeEnum.Collaborator)), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantAddedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PrimaryAssigneeCanAddParticipant_Returns201()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var existingParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = staffId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.PrimaryAssignee,
            CanPost = true,
            CanViewInternal = true,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { existingParticipant });

        var command = new ParticipantAddCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = staffId,
            ActorRole = ActorRoleEnum.Staff
        };

        var handler = new ParticipantAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Handle_NonManagerNonPrimaryAssignee_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = staffId,
            ActorRole = ActorRoleEnum.Staff
        };

        var handler = new ParticipantAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_DuplicateActiveParticipant_Returns400()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var existingParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = targetUserId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = managerId,
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { existingParticipant });

        var command = new ParticipantAddCommand
        {
            TicketId = ticketId,
            UserId = targetUserId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var command = new ParticipantAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            ActorUserId = Guid.NewGuid(),
            ActorRole = ActorRoleEnum.Manager
        };

        var handler = new ParticipantAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Validate_InvalidParticipantType_ReturnsError()
    {
        var command = new ParticipantAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ParticipantType = ParticipantTypeEnum.Owner
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ParticipantType");
    }
}
