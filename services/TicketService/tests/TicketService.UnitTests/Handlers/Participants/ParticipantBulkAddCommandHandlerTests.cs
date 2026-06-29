using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Participants;

public class ParticipantBulkAddCommandHandlerTests
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
    public async Task Handle_ManagerBulkAddsTwoUsers_Returns201()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantBulkAddCommand
        {
            TicketId = ticketId,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager,
            Participants = new List<ParticipantBulkAddItem>
            {
                new(userA, ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false),
                new(userB, ActorRoleEnum.Staff, ParticipantTypeEnum.Collaborator, true, true)
            }
        };

        var handler = new ParticipantBulkAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data.Should().HaveCount(2);

        participants.Verify(x => x.AddAsync(It.IsAny<TicketParticipant>()), Times.Exactly(2));
        uow.Verify(x => x.BeginTransactionAsync(), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<SharedContracts.Events.Chats.ParticipantAddedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_NonManagerActor_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ParticipantBulkAddCommand
        {
            TicketId = ticketId,
            ActorUserId = staffId,
            ActorRole = ActorRoleEnum.Staff,
            Participants = new List<ParticipantBulkAddItem>
            {
                new(Guid.NewGuid(), ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false)
            }
        };

        var handler = new ParticipantBulkAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_OneUserAlreadyActive_FailsEntireBatch()
    {
        var ticketId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        var existingParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = existingUserId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Watcher,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = managerId,
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, participants) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { existingParticipant });

        var command = new ParticipantBulkAddCommand
        {
            TicketId = ticketId,
            ActorUserId = managerId,
            ActorRole = ActorRoleEnum.Manager,
            Participants = new List<ParticipantBulkAddItem>
            {
                new(existingUserId, ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false),
                new(Guid.NewGuid(), ActorRoleEnum.Staff, ParticipantTypeEnum.Collaborator, true, true)
            }
        };

        var handler = new ParticipantBulkAddCommandHandler(uow.Object, _producer.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        participants.Verify(x => x.AddAsync(It.IsAny<TicketParticipant>()), Times.Never);
    }

    [Fact]
    public async Task Validate_DuplicateUserInBatch_ReturnsError()
    {
        var dupUserId = Guid.NewGuid();
        var command = new ParticipantBulkAddCommand
        {
            TicketId = Guid.NewGuid(),
            Participants = new List<ParticipantBulkAddItem>
            {
                new(dupUserId, ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false),
                new(dupUserId, ActorRoleEnum.Customer, ParticipantTypeEnum.Watcher, true, false)
            }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Detail.Contains("trùng"));
    }
}
