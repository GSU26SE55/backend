using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ChatFileSummaryQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();
    private readonly ChatFileSummaryQueryHandler _handler;

    public ChatFileSummaryQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_ticketsRepo.Object);
        _mockUow.Setup(x => x.TicketAttachments).Returns(_attachmentsRepo.Object);
        _handler = new ChatFileSummaryQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(Guid id, Guid customerId, Guid? staffId = null) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test Ticket",
        Description = "desc",
        CustomerId = customerId,
        AssignedStaffId = staffId,
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid ticketId, Ticket ticket, bool isInternal = false, bool isDeleted = false) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "msg",
        IsInternal = isInternal,
        IsDeleted = isDeleted,
        Ticket = ticket
    };

    private static TicketAttachment MakeAttachment(Guid ticketId, Ticket ticket, TicketChat chat) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        ChatId = chat.Id,
        UploadedByUserId = ticket.CustomerId,
        FileId = Guid.NewGuid(),
        FileName = "file.png",
        ContentType = "image/png",
        SizeBytes = 1024,
        Ticket = ticket,
        Chat = chat
    };

    private void SetupTickets(List<Ticket> tickets) =>
        _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    private void SetupAttachments(List<TicketAttachment> attachments) =>
        _attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketAttachment>(attachments));

    [Fact]
    public async Task Handle_CustomerActor_OnlySeesNonInternalChatFiles()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var publicChat = MakeChat(ticketId, ticket, isInternal: false);
        var internalChat = MakeChat(ticketId, ticket, isInternal: true);
        var publicAttachment = MakeAttachment(ticketId, ticket, publicChat);
        var internalAttachment = MakeAttachment(ticketId, ticket, internalChat);

        SetupTickets([ticket]);
        SetupAttachments([publicAttachment, internalAttachment]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].FileName.Should().Be("file.png");
        result.Data[0].ChatId.Should().Be(publicChat.Id.ToString());
    }

    [Fact]
    public async Task Handle_StaffActor_SeesAllFilesIncludingFromInternalChats()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId, staffId);
        var publicChat = MakeChat(ticketId, ticket, isInternal: false);
        var internalChat = MakeChat(ticketId, ticket, isInternal: true);
        var publicAttachment = MakeAttachment(ticketId, ticket, publicChat);
        var internalAttachment = MakeAttachment(ticketId, ticket, internalChat);

        SetupTickets([ticket]);
        SetupAttachments([publicAttachment, internalAttachment]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupTickets([]);
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
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
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_SoftDeletedChat_CustomerDoesNotSeeItsFiles()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);
        var deletedPublicChat = MakeChat(ticketId, ticket, isInternal: false, isDeleted: true);
        var attachment = MakeAttachment(ticketId, ticket, deletedPublicChat);

        SetupTickets([ticket]);
        SetupAttachments([attachment]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoAttachments_ReturnsEmptyList()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId);

        SetupTickets([ticket]);
        SetupAttachments([]);

        var result = await _handler.Handle(new ChatFileSummaryQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }
}
