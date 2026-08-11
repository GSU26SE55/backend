using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ChatGetByIdQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatMention>> _mentionsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatReaction>> _reactionsRepo = new();
    private readonly Mock<IGenericRepository<TicketChatTranslationUser>> _translationUsersRepo = new();
    private readonly ChatGetByIdQueryHandler _handler;

    public ChatGetByIdQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _mockUow.Setup(x => x.TicketAttachments).Returns(_attachmentsRepo.Object);
        _mockUow.Setup(x => x.TicketChatMentions).Returns(_mentionsRepo.Object);
        _mockUow.Setup(x => x.TicketChatReactions).Returns(_reactionsRepo.Object);
        _mockUow.Setup(x => x.ChatTranslationUsers).Returns(_translationUsersRepo.Object);
        _mentionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatMention>([]));
        _reactionsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatReaction>([]));
        _translationUsersRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChatTranslationUser>([]));
        _handler = new ChatGetByIdQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(Guid id, Guid? customerId = null, Guid? PrimaryHandlerStaffId = null) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test Ticket",
        Description = "desc",
        CustomerId = customerId ?? Guid.NewGuid(),
        PrimaryHandlerStaffId = PrimaryHandlerStaffId,
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, Ticket ticket, bool isInternal = false, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        IsInternal = isInternal,
        IsDeleted = isDeleted,
        Ticket = ticket
    };

    private void SetupTickets(List<Ticket> tickets) => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));
    private void SetupAttachments(List<TicketAttachment> attachments) => _attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketAttachment>(attachments));

    [Fact]
    public async Task Handle_CustomerOwnPublicChat_Returns200()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, customerId, ticket);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(chatId.ToString());
        result.Data!.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CustomerCannotSeeInternalChat_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, Guid.NewGuid(), ticket, isInternal: true);

        SetupTickets([ticket]);
        SetupChats([chat]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_DeletedChatRegularUser_ReturnsPlaceholder()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, customerId, ticket, isDeleted: true);

        SetupTickets([ticket]);
        SetupChats([chat]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.IsDeleted.Should().BeTrue();
        result.Data!.Body.Should().Be("This message has been deleted.");
        result.Data!.BodyHtml.Should().BeNull();
        result.Data!.AttachmentFileIds.Should().BeEmpty();
        result.Data!.Attachments.Should().BeEmpty();
        result.Data!.Mentions.Should().BeEmpty();
        result.Data!.ActiveTranslation.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DeletedChatAdmin_ReturnsRealContent()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, customerId, ticket, isDeleted: true);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.IsDeleted.Should().BeTrue();
        result.Data!.Body.Should().Be("Hello");
    }

    [Fact]
    public async Task Handle_DeletedChatManager_ReturnsRealContent()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(chatId, ticketId, customerId, ticket, isDeleted: true);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Manager"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.IsDeleted.Should().BeTrue();
        result.Data!.Body.Should().Be("Hello");
    }

    [Fact]
    public async Task Handle_ActorNotOnTicket_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);

        SetupTickets([ticket]);
        SetupChats([]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupTickets([]);

        var result = await _handler.Handle(new ChatGetByIdQuery
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
