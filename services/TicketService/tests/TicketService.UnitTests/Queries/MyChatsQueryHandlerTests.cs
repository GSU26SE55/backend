using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class MyChatsQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatMention>> _mentionsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatReaction>> _reactionsRepo = new();
    private readonly MyChatsQueryHandler _handler;

    public MyChatsQueryHandlerTests()
    {
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _mockUow.Setup(x => x.TicketChatMentions).Returns(_mentionsRepo.Object);
        _mockUow.Setup(x => x.TicketChatReactions).Returns(_reactionsRepo.Object);
        _mentionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatMention>(new List<TicketChatMention>()));
        _reactionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatReaction>(new List<TicketChatReaction>()));
        _handler = new MyChatsQueryHandler(_mockUow.Object);
    }

    private static TicketChat MakeChat(Guid authorId, Guid ticketId, bool isDeleted = false) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        IsDeleted = isDeleted,
        Ticket = new Ticket
        {
            Id = ticketId,
            Code = "T-001",
            Title = "Test Ticket",
            Description = "desc",
            CustomerId = authorId,
            Status = TicketStatusEnum.InProgress
        }
    };

    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));

    [Fact]
    public async Task Handle_ReturnsOnlyChatsAuthoredByUser_AcrossTickets()
    {
        var userId = Guid.NewGuid();
        var chatA = MakeChat(userId, Guid.NewGuid());
        var chatB = MakeChat(userId, Guid.NewGuid());
        var otherUserChat = MakeChat(Guid.NewGuid(), Guid.NewGuid());

        SetupChats([chatA, chatB, otherUserChat]);

        var result = await _handler.Handle(new MyChatsQuery { ActorUserId = userId, PageNumber = 1, PageSize = 10 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data!.TotalItems.Should().Be(2);
        result.Data!.Items.Should().OnlyContain(c => c.AuthorUserId == userId.ToString());
    }

    [Fact]
    public async Task Handle_ExcludesDeletedChats()
    {
        var userId = Guid.NewGuid();
        var deletedChat = MakeChat(userId, Guid.NewGuid(), isDeleted: true);

        SetupChats([deletedChat]);

        var result = await _handler.Handle(new MyChatsQuery { ActorUserId = userId, PageNumber = 1, PageSize = 10 }, default);

        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoChats_ReturnsEmptyPage()
    {
        SetupChats([]);

        var result = await _handler.Handle(new MyChatsQuery { ActorUserId = Guid.NewGuid(), PageNumber = 1, PageSize = 10 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data!.TotalItems.Should().Be(0);
    }
}
