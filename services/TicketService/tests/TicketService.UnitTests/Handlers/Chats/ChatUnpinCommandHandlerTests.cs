using MediatR;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatUnpinCommandHandlerTests
{
    // Sprint Chat DoD — bắt notification audit để khẳng định handler THỰC SỰ ghi vết.
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();

    private static readonly List<string> PinPermission = new() { ChatPermissionCodes.ChatPin };

    private ChatUnpinCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ChatUnpinCommandHandler(_uow.Object, _activityLogger.Object, new ChatAuthorizationService(_uow.Object), _publisher.Object);
    }

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description"
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Ticket ticket, bool isPinned = true, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Chat body",
        Ticket = ticket,
        IsPinned = isPinned,
        PinnedAt = isPinned ? DateTime.UtcNow : null,
        PinnedByUserId = isPinned ? Guid.NewGuid() : null,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task Handle_ValidUnpin_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatUnpinCommand
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

        // Sprint Chat DoD — chat.unpinned phải sinh audit trail.
        _publisher.Verify(x => x.Publish(
            It.Is<TicketAuditTrailNotification>(nn => nn.ActionCode == nameof(TicketAuditActionEnum.ChatUnpinned)),
            It.IsAny<CancellationToken>()), Times.Once);
        result.StatusCode.Should().Be(200);
        chat.IsPinned.Should().BeFalse();
        chat.PinnedAt.Should().BeNull();
        chat.PinnedByUserId.Should().BeNull();

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, userId, ActorRoleEnum.Manager, "Manager",
            ActivityActionEnum.ChatUnpinned, It.IsAny<string>(), null, null), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotPinned_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket, isPinned: false);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatUnpinCommand
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
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);

        var handler = CreateHandler();
        var command = new ChatUnpinCommand
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
    public async Task Handle_WithoutPinPermission_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatUnpinCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            UserPermissions = new List<string>()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyIds_ReturnsErrors()
    {
        var command = new ChatUnpinCommand
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
