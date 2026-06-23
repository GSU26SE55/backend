using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.ChatReplies;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class ChatRepliesQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly ChatRepliesQueryHandler _handler;

    public ChatRepliesQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _handler = new ChatRepliesQueryHandler(_mockUow.Object);
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

    private static TicketChat MakeChat(Guid id, Guid ticketId, Ticket ticket, Guid? parentChatId = null, bool isInternal = false, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = ticket.CustomerId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        IsInternal = isInternal,
        ParentChatId = parentChatId,
        Ticket = ticket,
        IsDeleted = isDeleted
    };

    private void SetupTickets(List<Ticket> tickets) => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));

    [Fact]
    public async Task Handle_ReturnsRepliesOfParent()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var parent = MakeChat(parentId, ticketId, ticket);
        var reply = MakeChat(Guid.NewGuid(), ticketId, ticket, parentChatId: parentId);

        SetupTickets([ticket]);
        SetupChats([parent, reply]);

        var result = await _handler.Handle(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data!.Items[0].ParentChatId.Should().Be(parentId.ToString());
    }

    [Fact]
    public async Task Handle_CustomerCannotSeeInternalReplies()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var parent = MakeChat(parentId, ticketId, ticket);
        var internalReply = MakeChat(Guid.NewGuid(), ticketId, ticket, parentChatId: parentId, isInternal: true);

        SetupTickets([ticket]);
        SetupChats([parent, internalReply]);

        var result = await _handler.Handle(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SoftDeletedParent_StillReturnsReplies()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var parent = MakeChat(parentId, ticketId, ticket, isDeleted: true);
        var reply = MakeChat(Guid.NewGuid(), ticketId, ticket, parentChatId: parentId);

        SetupTickets([ticket]);
        SetupChats([parent, reply]);

        var result = await _handler.Handle(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ParentNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);

        SetupTickets([ticket]);
        SetupChats([]);

        var result = await _handler.Handle(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = Guid.NewGuid(),
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActorWithoutTicketAccess_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, Guid.NewGuid());
        var parent = MakeChat(parentId, ticketId, ticket);

        SetupTickets([ticket]);
        SetupChats([parent]);

        var result = await _handler.Handle(new ChatRepliesQuery
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
