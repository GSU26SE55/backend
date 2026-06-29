using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ChatAttachmentListQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();
    private readonly ChatAttachmentListQueryHandler _handler;

    public ChatAttachmentListQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketChats).Returns(_chatsRepo.Object);
        _mockUow.Setup(x => x.TicketAttachments).Returns(_attachmentsRepo.Object);
        _handler = new ChatAttachmentListQueryHandler(_mockUow.Object);
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

    private static TicketChat MakeChat(Guid id, Guid ticketId, Ticket ticket, bool isInternal = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = ticket.CustomerId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        IsInternal = isInternal,
        Ticket = ticket
    };

    private static TicketAttachment MakeAttachment(Guid chatId, Guid ticketId, Ticket ticket) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        ChatId = chatId,
        UploadedByUserId = ticket.CustomerId,
        FileId = Guid.NewGuid(),
        FileName = "file.png",
        ContentType = "image/png",
        SizeBytes = 1024,
        Ticket = ticket
    };

    private void SetupTickets(List<Ticket> tickets) => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));
    private void SetupChats(List<TicketChat> chats) => _chatsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketChat>(chats));
    private void SetupAttachments(List<TicketAttachment> attachments) => _attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketAttachment>(attachments));

    [Fact]
    public async Task Handle_ReturnsAttachmentsForChat()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(Guid.NewGuid(), ticketId, ticket);
        var attachment = MakeAttachment(chat.Id, ticketId, ticket);

        SetupTickets([ticket]);
        SetupChats([chat]);
        SetupAttachments([attachment]);

        var result = await _handler.Handle(new ChatAttachmentListQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].FileName.Should().Be("file.png");
    }

    [Fact]
    public async Task Handle_CustomerCannotListInternalChatAttachments_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var chat = MakeChat(Guid.NewGuid(), ticketId, ticket, isInternal: true);

        SetupTickets([ticket]);
        SetupChats([chat]);

        var result = await _handler.Handle(new ChatAttachmentListQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
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
        var ticket = MakeTicket(ticketId, Guid.NewGuid());

        SetupTickets([ticket]);
        SetupChats([]);

        var result = await _handler.Handle(new ChatAttachmentListQuery
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
