using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class ChatHistoryQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatEdit>> _editsRepo = new();
    private readonly ChatHistoryQueryHandler _handler;

    public ChatHistoryQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _mockUow.Setup(x => x.TicketChatEdits).Returns(_editsRepo.Object);
        _handler = new ChatHistoryQueryHandler(_mockUow.Object);
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

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, ActorRoleEnum authorRole, Ticket ticket) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = authorRole,
        Body = "Hello",
        Ticket = ticket
    };

    private static TicketChatEdit MakeEdit(Guid chatId, TicketChat chat) => new()
    {
        Id = Guid.NewGuid(),
        ChatId = chatId,
        Chat = chat,
        OldBody = "old",
        NewBody = "new",
        EditedAt = DateTime.UtcNow,
        EditedByUserId = chat.AuthorUserId,
        EditedByRole = chat.AuthorRole
    };

    private void SetupTickets(List<Ticket> tickets) => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));
    private void SetupEdits(List<TicketChatEdit> edits) => _editsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatEdit>(edits));

    [Fact]
    public async Task Handle_AuthorViewsOwnChatHistory_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, customerId, ActorRoleEnum.Customer, ticket);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupEdits([MakeEdit(chatId, chat)]);

        var result = await _handler.Handle(new ChatHistoryQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_CustomerCannotSeeStaffChatHistory_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        ticket.AssignedStaffId = staffId;
        var chat = MakeChat(Guid.NewGuid(), ticketId, staffId, ActorRoleEnum.Staff, ticket);

        SetupTickets([ticket]);
        SetupChats([chat]);

        var result = await _handler.Handle(new ChatHistoryQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_StaffCanSeeOtherAuthorsHistory_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        ticket.AssignedStaffId = staffId;
        var chat = MakeChat(Guid.NewGuid(), ticketId, customerId, ActorRoleEnum.Customer, ticket);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupEdits([]);

        var result = await _handler.Handle(new ChatHistoryQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ChatNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);

        SetupTickets([ticket]);
        SetupChats([]);

        var result = await _handler.Handle(new ChatHistoryQuery
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
