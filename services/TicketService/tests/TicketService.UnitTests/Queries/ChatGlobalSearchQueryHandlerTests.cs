using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.ChatGlobalSearch;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class ChatGlobalSearchQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatMention>> _mentionsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatReaction>> _reactionsRepo = new();
    private readonly ChatGlobalSearchQueryHandler _handler;

    public ChatGlobalSearchQueryHandlerTests()
    {
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupMentions();
        _uow.SetupReactions();

        _handler = new ChatGlobalSearchQueryHandler(_uow.Object);
    }

    private void SetupChats(List<TicketChat> chats) =>
        _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));

    private void SetupTickets(List<Ticket> tickets) =>
        _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    private static readonly Ticket _dummyTicket = new()
    {
        Id = Guid.NewGuid(),
        Code = "T-000",
        Title = "dummy",
        Description = "dummy",
        CustomerId = Guid.NewGuid(),
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid ticketId, string body, bool isInternal = false) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Staff,
        Body = body,
        IsInternal = isInternal,
        AttachmentFileIds = new List<Guid>(),
        Ticket = _dummyTicket
    };

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllNonDeletedChats()
    {
        var ticketId = Guid.NewGuid();
        SetupChats([MakeChat(ticketId, "First"), MakeChat(ticketId, "Second")]);
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGlobalSearchQuery
        {
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithKeyword_FiltersChats()
    {
        var ticketId = Guid.NewGuid();
        SetupChats([MakeChat(ticketId, "Hello world"), MakeChat(ticketId, "Goodbye")]);
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGlobalSearchQuery
        {
            Q = "Hello",
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items[0].Body.Should().Be("Hello world");
    }

    [Fact]
    public async Task Handle_CustomerRole_FiltersInternalChats()
    {
        var ticketId = Guid.NewGuid();
        SetupChats([
            MakeChat(ticketId, "Public chat", isInternal: false),
            MakeChat(ticketId, "Internal chat", isInternal: true)
        ]);
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGlobalSearchQuery
        {
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items[0].Body.Should().Be("Public chat");
    }

    [Fact]
    public async Task Handle_AdminRole_CanSeeInternalChats()
    {
        var ticketId = Guid.NewGuid();
        SetupChats([
            MakeChat(ticketId, "Public chat", isInternal: false),
            MakeChat(ticketId, "Internal chat", isInternal: true)
        ]);
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGlobalSearchQuery
        {
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Handle_WithDateRange_FiltersChats()
    {
        var ticketId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var oldChat = MakeChat(ticketId, "Old");
        oldChat.CreatedAt = now.AddDays(-10);

        var recentChat = MakeChat(ticketId, "Recent");
        recentChat.CreatedAt = now.AddDays(-1);

        SetupChats([oldChat, recentChat]);
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGlobalSearchQuery
        {
            DateFrom = now.AddDays(-3),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items[0].Body.Should().Be("Recent");
    }
}
