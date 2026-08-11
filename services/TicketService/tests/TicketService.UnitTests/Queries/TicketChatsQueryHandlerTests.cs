using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketChatsQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketParticipant>> _participantsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatMention>> _mentionsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatReaction>> _reactionsRepo = new();
    private readonly Mock<IChatCacheService> _chatCache = new();
    private readonly TicketChatsQueryHandler _handler;

    public TicketChatsQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _mockUow.Setup(x => x.TicketParticipants).Returns(_participantsRepo.Object);
        _mockUow.Setup(x => x.TicketChatMentions).Returns(_mentionsRepo.Object);
        _mockUow.Setup(x => x.TicketChatReactions).Returns(_reactionsRepo.Object);
        _mentionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatMention>(new List<TicketChatMention>()));
        _reactionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatReaction>(new List<TicketChatReaction>()));
        _mockUow.SetupChatTranslationUsers();
        _mockUow.SetupChatHides();
        _handler = new TicketChatsQueryHandler(_mockUow.Object, _chatCache.Object);
    }

    private static Ticket MakeTicket(Guid id, Guid customerId) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test Ticket",
        Description = "desc",
        CustomerId = customerId,
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Ticket ticket, DateTime createdAt, bool isPinned = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = ticket.CustomerId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        CreatedAt = createdAt,
        Ticket = ticket,
        IsPinned = isPinned
    };

    private void SetupTickets(List<Ticket> tickets) => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));
    private void SetupParticipants(List<TicketParticipant> participants) => _participantsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketParticipant>(participants));

    [Fact]
    public async Task Handle_DeletedChat_IncludedWithPlaceholderBody()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var now = DateTime.UtcNow;

        var deletedChat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            AuthorUserId = customerId,
            AuthorRole = ActorRoleEnum.Customer,
            Body = "Original content",
            CreatedAt = now,
            IsDeleted = true,
            Ticket = ticket,
            AttachmentFileIds = new List<Guid>()
        };
        var normalChat = MakeChat(Guid.NewGuid(), ticketId, ticket, now.AddMinutes(-1));

        SetupTickets([ticket]);
        SetupChats([deletedChat, normalChat]);
        SetupParticipants([]);

        var result = await _handler.Handle(new TicketChatsQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);

        var deleted = result.Data.Items.Single(c => c.IsDeleted);
        deleted.Body.Should().Be("This message has been deleted.");
        deleted.AttachmentFileIds.Should().BeEmpty();
        deleted.Mentions.Should().BeEmpty();
        deleted.ActiveTranslation.Should().BeNull();

        var normal = result.Data.Items.Single(c => !c.IsDeleted);
        normal.Body.Should().Be("Hello");
    }

    [Fact]
    public async Task Handle_PinnedChatsSortedFirst_RegardlessOfCreatedAt()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var now = DateTime.UtcNow;

        var oldPinned = MakeChat(Guid.NewGuid(), ticketId, ticket, now.AddHours(-5), isPinned: true);
        var newUnpinned = MakeChat(Guid.NewGuid(), ticketId, ticket, now, isPinned: false);

        SetupTickets([ticket]);
        SetupChats([newUnpinned, oldPinned]);
        SetupParticipants([]);

        var result = await _handler.Handle(new TicketChatsQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data!.Items[0].Id.Should().Be(oldPinned.Id.ToString());
        result.Data!.Items[0].IsPinned.Should().BeTrue();
        result.Data!.Items[1].Id.Should().Be(newUnpinned.Id.ToString());
    }
}
