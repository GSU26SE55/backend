using MediatR;
using TicketService.Application.CQRS.Notification.Audit;
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

public class ChatEditCommandHandlerTests
{
    // Sprint Chat DoD — bắt notification audit để khẳng định handler THỰC SỰ ghi vết.
    private readonly Mock<IPublisher> _publisher = new();
    private IReadOnlyList<string> NoMatches = Array.Empty<string>();

    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatEdit>> _chatEditsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<IMarkdownRenderer> _markdownRenderer = new();
    private readonly Mock<IProfanityFilter> _profanityFilter = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly IOptions<ChatOptions> _chatOptions = Options.Create(new ChatOptions());
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<IChatCacheService> _chatCache = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<ChatEditCommandHandler>> _logger = new();

    public ChatEditCommandHandlerTests()
    {
        _profanityFilter.Setup(x => x.ContainsProfanity(It.IsAny<string>(), out NoMatches)).Returns(false);
        _piiDetector.Setup(x => x.ContainsPii(It.IsAny<string>(), out NoMatches)).Returns(false);
    }

    private ChatEditCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.TicketChatEdits).Returns(_chatEditsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.SetupChatTranslations();
        var chatAuthorizationService = new ChatAuthorizationService(_uow.Object);
        return new ChatEditCommandHandler(
            _uow.Object, _activityLogger.Object, _markdownRenderer.Object,
            chatAuthorizationService, _profanityFilter.Object, _piiDetector.Object, _chatOptions,
            _outboxWriter.Object, _realtimeNotifier.Object, _chatCache.Object, _cache.Object, _logger.Object,
            _publisher.Object);
    }

    private static Ticket MakeTicket(Guid id, TicketStatusEnum status = TicketStatusEnum.InProgress) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description",
        Status = status
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, DateTime createdAt, Ticket ticket, ChatBodyFormatEnum bodyFormat = ChatBodyFormatEnum.PlainText) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Original body",
        CreatedAt = createdAt,
        Ticket = ticket,
        BodyFormat = bodyFormat
    };

    [Fact]
    public async Task Handle_AuthorEditWithinWindow_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Sprint Chat DoD — chat.edited phải sinh audit trail.
        _publisher.Verify(x => x.Publish(
            It.Is<TicketAuditTrailNotification>(nn => nn.ActionCode == nameof(TicketAuditActionEnum.ChatEdited)),
            It.IsAny<CancellationToken>()), Times.Once);
        result.StatusCode.Should().Be(200);
        chat.Body.Should().Be("Edited body");
        chat.EditCount.Should().Be(1);
        chat.LastEditedByUserId.Should().Be(authorId);

        _chatEditsRepo.Verify(r => r.AddAsync(It.Is<TicketChatEdit>(e =>
            e.ChatId == chatId && e.OldBody == "Original body" && e.NewBody == "Edited body" && e.EditReason == null)), Times.Once);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, authorId, ActorRoleEnum.Customer, "Author",
            ActivityActionEnum.ChatEdited, It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MarkdownChatEdited_RerendersBodyHtml()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket, ChatBodyFormatEnum.Markdown);
        chat.BodyHtml = "<p>Original body</p>";

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _markdownRenderer.Setup(r => r.RenderToHtml("Edited **body**", chat.AttachmentFileIds)).Returns("<p>Edited <strong>body</strong></p>");

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited **body**"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chat.BodyHtml.Should().Be("<p>Edited <strong>body</strong></p>");
        _markdownRenderer.Verify(r => r.RenderToHtml("Edited **body**", chat.AttachmentFileIds), Times.Once);
    }

    [Fact]
    public async Task Handle_AuthorEditAfterWindow_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-20), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.Body.Should().Be("Original body");
    }

    [Fact]
    public async Task Handle_ManagerEditOthersChat_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-60), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = managerId,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            Body = "Edited by manager",
            UserPermissions = new List<string>()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.Body.Should().Be("Original body");
    }

    [Fact]
    public async Task Handle_NonAuthorWithoutEditAnyPermission_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = otherStaffId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Other Staff",
            Body = "Trying to edit"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.Closed);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_TicketClosedPendingRate_AuthorStillBlocked()
    {
        // #517 — ClosedPendingRate chỉ miễn cho hành động Add (Customer), Edit luôn bị chặn dù là Author.
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.ClosedPendingRate);
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
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
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
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
        var chat = MakeChat(chatId, ticketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync((Ticket?)null);

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
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
        var chat = MakeChat(chatId, actualTicketId, authorId, DateTime.UtcNow.AddMinutes(-5), ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(otherTicketId)).ReturnsAsync(MakeTicket(otherTicketId));

        var handler = CreateHandler();
        var command = new ChatEditCommand
        {
            TicketId = otherTicketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = "Edited body"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        chat.Body.Should().Be("Original body");
        _chatEditsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChatEdit>()), Times.Never);
    }

    [Fact]
    public async Task Validate_EmptyBody_ReturnsError()
    {
        var command = new ChatEditCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = ""
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }

    [Fact]
    public async Task Validate_BodyTooLong_ReturnsError()
    {
        var command = new ChatEditCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            Body = new string('a', 10001)
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }

}
