using Moq;
using TicketService.Application.CQRS.Command.ChatPin;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatPinCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _activityLogger = new();

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description"
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Ticket ticket, bool isPinned = false, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Chat body",
        Ticket = ticket,
        IsPinned = isPinned,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task Handle_ValidPin_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket);

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { chat }
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = userId,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        chat.IsPinned.Should().BeTrue();
        chat.PinnedByUserId.Should().Be(userId);
        chat.PinnedAt.Should().NotBeNull();

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, userId, ActorRoleEnum.Manager, "Manager",
            ActivityActionEnum.ChatPinned, null, It.IsAny<string>(), null), Times.Once);

        uow.Verify(u => u.BeginTransactionAsync(), Times.Once);
        uow.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyPinned_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket, isPinned: true);

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { chat }
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_AlreadyAtMaxPinnedLimit_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket);
        var pinned1 = MakeChat(Guid.NewGuid(), ticketId, ticket, isPinned: true);
        var pinned2 = MakeChat(Guid.NewGuid(), ticketId, ticket, isPinned: true);
        var pinned3 = MakeChat(Guid.NewGuid(), ticketId, ticket, isPinned: true);

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { chat, pinned1, pinned2, pinned3 }
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chat.IsPinned.Should().BeFalse();
        uow.Verify(u => u.RollbackTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChatBelongsToDifferentTicket_ReturnsNotFound()
    {
        var actualTicketId = Guid.NewGuid();
        var otherTicketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(actualTicketId);
        var chat = MakeChat(chatId, actualTicketId, ticket);

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket, MakeTicket(otherTicketId) },
            chatSeed: new[] { chat }
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatPinCommand
        {
            TicketId = otherTicketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        chat.IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyIds_ReturnsErrors()
    {
        var command = new ChatPinCommand
        {
            TicketId = Guid.Empty,
            ChatId = Guid.Empty,
            UserId = Guid.Empty,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager"
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "TicketId");
        result.ListErrors.Should().Contain(e => e.Field == "ChatId");
        result.ListErrors.Should().Contain(e => e.Field == "UserId");
    }
}
