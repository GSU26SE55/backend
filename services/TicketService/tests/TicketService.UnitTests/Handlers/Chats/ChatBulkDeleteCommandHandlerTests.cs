using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
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
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatBulkDeleteCommandHandlerTests
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
    private readonly Mock<ILogger<ChatBulkDeleteCommandHandler>> _logger = new();

    private ChatBulkDeleteCommandHandler CreateHandler(List<TicketChat> chatSeed)
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => chatSeed.AsQueryable().BuildMock());
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _uow.SetupChatTranslations();
        _uow.SetupChatHides();
        return new ChatBulkDeleteCommandHandler(
            _uow.Object, _activityLogger.Object, _chatOptions,
            _outboxWriter.Object, _realtimeNotifier.Object,
            _chatCache.Object, _cache.Object, _logger.Object);
    }

    private static Ticket MakeTicket(Guid id, TicketStatusEnum status = TicketStatusEnum.InProgress) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test",
        Description = "Test",
        Status = status
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId)
    {
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test", Description = "Test", Status = TicketStatusEnum.InProgress };
        return new TicketChat
        {
            Id = id,
            TicketId = ticketId,
            AuthorUserId = authorId,
            AuthorRole = ActorRoleEnum.Customer,
            Body = "Hello",
            IsDeleted = false,
            Ticket = ticket
        };
    }

    [Fact]
    public async Task Handle_AuthorDeletesOwnChats_AllDeleted()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var chat1 = MakeChat(Guid.NewGuid(), ticketId, authorId);
        var chat2 = MakeChat(Guid.NewGuid(), ticketId, authorId);
        var ticket = MakeTicket(ticketId);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        var handler = CreateHandler(new List<TicketChat> { chat1, chat2 });

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { chat1.Id, chat2.Id }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Deleted.Should().Be(2);
        result.Data.Skipped.Should().Be(0);
        chat1.IsDeleted.Should().BeTrue();
        chat2.IsDeleted.Should().BeTrue();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MixedOwnership_SkipsOtherAuthorChats()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var ownChat = MakeChat(Guid.NewGuid(), ticketId, authorId);
        var otherChat = MakeChat(Guid.NewGuid(), ticketId, otherId);
        var ticket = MakeTicket(ticketId);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        var handler = CreateHandler(new List<TicketChat> { ownChat, otherChat });

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { ownChat.Id, otherChat.Id }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Deleted.Should().Be(1);
        result.Data.Skipped.Should().Be(0);
        ownChat.IsDeleted.Should().BeTrue();
        otherChat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AllChatsFromOtherAuthor_Returns_ZeroDeleted()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var chat = MakeChat(Guid.NewGuid(), ticketId, otherId);
        var ticket = MakeTicket(ticketId);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        var handler = CreateHandler(new List<TicketChat> { chat });

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { chat.Id }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Deleted.Should().Be(0);
        result.Data.Skipped.Should().Be(0);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsNotFound()
    {
        _ticketsRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((Ticket?)null);
        var handler = CreateHandler(new List<TicketChat>());

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.Closed);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        var handler = CreateHandler(new List<TicketChat>());

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedChatsAreIgnored()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var chat = MakeChat(Guid.NewGuid(), ticketId, authorId);
        chat.IsDeleted = true;
        var ticket = MakeTicket(ticketId);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        // Already-deleted chats are filtered by !x.IsDeleted in handler query, so not returned
        var handler = CreateHandler(new List<TicketChat>());

        var result = await handler.Handle(new ChatBulkDeleteCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid> { chat.Id }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Deleted.Should().Be(0);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Validate_EmptyChatIds_ReturnsError()
    {
        var command = new ChatBulkDeleteCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = new List<Guid>()
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ChatIds");
    }

    [Fact]
    public async Task Validate_TooManyChatIds_ReturnsError()
    {
        var command = new ChatBulkDeleteCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Author",
            ChatIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList()
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "ChatIds");
    }
}
