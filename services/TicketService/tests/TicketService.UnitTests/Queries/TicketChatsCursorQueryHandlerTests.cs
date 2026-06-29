using System.Text;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketChatsCursorQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketParticipant>> _participantsRepo = new();
    private readonly TicketChatsCursorQueryHandler _handler;

    public TicketChatsCursorQueryHandlerTests()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.TicketParticipants).Returns(_participantsRepo.Object);
        _uow.SetupMentions();
        _uow.SetupReactions();
        _uow.SetupChatTranslationUsers();

        _participantsRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketParticipant>(new List<TicketParticipant>()));

        _handler = new TicketChatsCursorQueryHandler(_uow.Object);
    }

    private static Ticket MakeTicket(Guid id, Guid customerId) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test",
        Description = "desc",
        CustomerId = customerId,
        Status = TicketStatusEnum.InProgress
    };

    private static readonly Ticket _dummyTicket = new()
    {
        Id = Guid.NewGuid(),
        Code = "T-000",
        Title = "dummy",
        Description = "dummy",
        CustomerId = Guid.NewGuid(),
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid ticketId, string body, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Staff,
        Body = body,
        IsInternal = false,
        CreatedAt = createdAt,
        AttachmentFileIds = new List<Guid>(),
        Ticket = _dummyTicket
    };

    private void SetupTickets(List<Ticket> tickets) =>
        _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    private void SetupChats(List<TicketChat> chats) =>
        _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));

    private static string EncodeCursor(Guid chatId, DateTime createdAt)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{chatId}:{createdAt.Ticks}"));

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupTickets([]);

        var result = await _handler.Handle(new TicketChatsCursorQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Forbidden_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, ownerId);
        SetupTickets([ticket]);
        SetupChats([]);

        var result = await _handler.Handle(new TicketChatsCursorQuery
        {
            TicketId = ticketId,
            ActorUserId = strangerId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_NoCursor_ReturnsFirstPageDescending()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var now = DateTime.UtcNow;

        var older = MakeChat(ticketId, "Older", now.AddMinutes(-5));
        var newer = MakeChat(ticketId, "Newer", now);

        SetupTickets([ticket]);
        SetupChats([older, newer]);

        var result = await _handler.Handle(new TicketChatsCursorQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"],
            Limit = 10
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items[0].Body.Should().Be("Newer");
        result.Data.Items[1].Body.Should().Be("Older");
        result.Data.HasMore.Should().BeFalse();
        result.Data.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LimitReached_ReturnsHasMoreTrue()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var now = DateTime.UtcNow;

        var chats = Enumerable.Range(0, 5)
            .Select(i => MakeChat(ticketId, $"Chat {i}", now.AddMinutes(-i)))
            .ToList();

        SetupTickets([ticket]);
        SetupChats(chats);

        var result = await _handler.Handle(new TicketChatsCursorQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"],
            Limit = 3
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(3);
        result.Data.HasMore.Should().BeTrue();
        result.Data.NextCursor.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_InvalidCursor_TreatsAsFirstPage()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var now = DateTime.UtcNow;

        SetupTickets([ticket]);
        SetupChats([MakeChat(ticketId, "Chat", now)]);

        var result = await _handler.Handle(new TicketChatsCursorQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"],
            Cursor = "not-valid-base64!!!"
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
    }
}
