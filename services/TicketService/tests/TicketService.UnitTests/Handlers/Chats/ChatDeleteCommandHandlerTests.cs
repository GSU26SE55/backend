using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatDeleteCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly IOptions<ChatOptions> _chatOptions = Options.Create(new ChatOptions());
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<IChatCacheService> _chatCache = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<ChatDeleteCommandHandler>> _logger = new();

    private ChatDeleteCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.SetupChatTranslations();
        _uow.SetupChatHides();
        var chatAuthorizationService = new ChatAuthorizationService(_uow.Object);
        return new ChatDeleteCommandHandler(_uow.Object, _activityLogger.Object, chatAuthorizationService, _chatOptions, _outboxWriter.Object, _realtimeNotifier.Object, _chatCache.Object, _cache.Object, _logger.Object);
    }

    private static Ticket MakeTicket(Guid id, TicketStatusEnum status = TicketStatusEnum.InProgress) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description",
        Status = status
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, Ticket ticket, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Original body",
        Ticket = ticket,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task Handle_AuthorDeleteOwnChat_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        chat.IsDeleted.Should().BeTrue();
        chat.DeletedAt.Should().NotBeNull();

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, authorId, ActorRoleEnum.Customer, "Author",
            ActivityActionEnum.ChatDeleted, It.IsAny<string>(), null, null), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ManagerDeleteOthersChat_HidesForCaller()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = managerId,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            UserPermissions = new List<string>()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Message.Should().Be("Đã ẩn bình luận.");
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AdminDeleteOthersChat_HidesForCaller()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            UserPermissions = new List<string>()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Message.Should().Be("Đã ẩn bình luận.");
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonAuthorWithoutDeleteAnyPermission_HidesForCaller()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = otherStaffId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Other Staff"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Message.Should().Be("Đã ẩn bình luận.");
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.Closed);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_TicketClosedPendingRate_AuthorStillBlocked()
    {
        // #517 — ClosedPendingRate chỉ miễn cho hành động Add (Customer), Delete luôn bị chặn dù là Author.
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.ClosedPendingRate);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChatAlreadyDeleted_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket, isDeleted: true);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync((Ticket?)null);

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
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
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(actualTicketId);
        var chat = MakeChat(chatId, actualTicketId, authorId, ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(otherTicketId)).ReturnsAsync(MakeTicket(otherTicketId));

        var handler = CreateHandler();
        var command = new ChatDeleteCommand
        {
            TicketId = otherTicketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyIds_ReturnsErrors()
    {
        var command = new ChatDeleteCommand
        {
            TicketId = Guid.Empty,
            ChatId = Guid.Empty,
            UserId = Guid.Empty,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author"
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "TicketId");
        result.ListErrors.Should().Contain(e => e.Field == "ChatId");
        result.ListErrors.Should().Contain(e => e.Field == "UserId");
    }
}
