using Microsoft.Extensions.Logging;
using Moq;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatReactionRemoveCommandHandlerTests
{
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<ILogger<ChatReactionRemoveCommandHandler>> _logger = new();

    private static TicketChat MakeChat(Guid ticketId, Guid id, Guid authorId) => new()
    {
        Id = id,
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Pin sạc rất chậm"
    };

    private static (Mock<ITicketUnitOfWork> uow, Mock<IGenericRepository<TicketChatReaction>> reactionsRepo) BuildUow(
        TicketChat chat, List<TicketChatReaction> reactionSeed)
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { chat.Ticket! });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        var reactionsRepo = uow.SetupReactions(reactionSeed);
        return (uow, reactionsRepo);
    }

    [Fact]
    public async Task Handle_ExistingReaction_RemovesAndPublishesEvent()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid(), authorId);
        var userId = Guid.NewGuid();

        var existing = new TicketChatReaction
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Chat = chat,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ReactionType = ReactionTypeEnum.ThumbsUp
        };

        var (uow, reactionsRepo) = BuildUow(chat, new List<TicketChatReaction> { existing });
        var handler = new ChatReactionRemoveCommandHandler(uow.Object, _outboxWriter.Object, _realtimeNotifier.Object, _logger.Object);

        var command = new ChatReactionRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ReactionType = ReactionTypeEnum.ThumbsUp
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ThumbsUp.Count.Should().Be(0);
        existing.IsDeleted.Should().BeTrue();

        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<ChatReactedEvent>(e => e.ChatId == chat.Id && e.IsRemoved == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReactionDoesNotExist_NoOpButStillSucceeds()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid(), authorId);

        var (uow, _) = BuildUow(chat, new List<TicketChatReaction>());
        var handler = new ChatReactionRemoveCommandHandler(uow.Object, _outboxWriter.Object, _realtimeNotifier.Object, _logger.Object);

        var command = new ChatReactionRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ReactionType = ReactionTypeEnum.ThumbsUp
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<ChatReactedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReactions(new List<TicketChatReaction>());

        var handler = new ChatReactionRemoveCommandHandler(uow.Object, _outboxWriter.Object, _realtimeNotifier.Object, _logger.Object);

        var command = new ChatReactionRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ReactionType = ReactionTypeEnum.ThumbsUp
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActorWithoutTicketAccess_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid(), authorId);
        chat.Ticket!.CustomerId = Guid.NewGuid();
        chat.Ticket!.PrimaryHandlerStaffId = Guid.NewGuid();

        var (uow, _) = BuildUow(chat, new List<TicketChatReaction>());
        var handler = new ChatReactionRemoveCommandHandler(uow.Object, _outboxWriter.Object, _realtimeNotifier.Object, _logger.Object);

        var command = new ChatReactionRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            ActorRoles = new[] { "Customer" },
            ReactionType = ReactionTypeEnum.ThumbsUp
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<ChatReactedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
