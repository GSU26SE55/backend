using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatPinCommandHandlerTests
{
    // Sprint Chat DoD — bắt notification audit để khẳng định handler THỰC SỰ ghi vết.
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<ILogger<ChatPinCommandHandler>> _logger = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();

    private static readonly List<string> PinPermission = new() { ChatPermissionCodes.ChatPin };

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

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = userId,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = PinPermission
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Sprint Chat DoD — chat.pinned phải sinh audit trail.
        _publisher.Verify(x => x.Publish(
            It.Is<TicketAuditTrailNotification>(nn => nn.ActionCode == nameof(TicketAuditActionEnum.ChatPinned)),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Handle_WithoutPinPermission_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket);

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { chat }
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.IsPinned.Should().BeFalse();
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

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = PinPermission
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
        // MaxPinnedPerTicket is 5 — seed exactly that many so the next pin is the one rejected.
        var alreadyPinned = Enumerable.Range(0, 5)
            .Select(_ => MakeChat(Guid.NewGuid(), ticketId, ticket, isPinned: true))
            .ToArray();

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { chat }.Concat(alreadyPinned).ToArray()
        );
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = PinPermission
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

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = PinPermission
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

        var handler = new ChatPinCommandHandler(uow.Object, _activityLogger.Object, new ChatAuthorizationService(uow.Object), _publisher.Object, _realtimeNotifier.Object, _logger.Object);
        var command = new ChatPinCommand
        {
            TicketId = otherTicketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = PinPermission
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
