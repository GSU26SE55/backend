using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatAttachmentRemove;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatAttachmentRemoveCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();

    private ChatAttachmentRemoveCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.TicketAttachments).Returns(_attachmentsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ChatAttachmentRemoveCommandHandler(_uow.Object);
    }

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test Ticket",
        Description = "desc",
        Status = TicketStatusEnum.InProgress
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, Ticket ticket) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Hello",
        Ticket = ticket
    };

    private static TicketAttachment MakeAttachment(Guid id, Guid ticketId, Guid chatId, Ticket ticket) => new()
    {
        Id = id,
        TicketId = ticketId,
        ChatId = chatId,
        UploadedByUserId = ticket.CustomerId,
        FileId = Guid.NewGuid(),
        FileName = "file.png",
        ContentType = "image/png",
        SizeBytes = 1024,
        Ticket = ticket
    };

    [Fact]
    public async Task Handle_AuthorRemovesOwnAttachment_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _attachmentsRepo.Setup(r => r.GetByIdAsync(attachmentId)).ReturnsAsync(attachment);

        var handler = CreateHandler();
        var result = await handler.Handle(new ChatAttachmentRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        attachment.IsDeleted.Should().BeTrue();
        _attachmentsRepo.Verify(r => r.UpdateAsync(attachment), Times.Once);
    }

    [Fact]
    public async Task Handle_NonAuthorNonManager_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _attachmentsRepo.Setup(r => r.GetByIdAsync(attachmentId)).ReturnsAsync(attachment);

        var handler = CreateHandler();
        var result = await handler.Handle(new ChatAttachmentRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        attachment.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ManagerRemovesOthersAttachment_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _attachmentsRepo.Setup(r => r.GetByIdAsync(attachmentId)).ReturnsAsync(attachment);

        var handler = CreateHandler();
        var result = await handler.Handle(new ChatAttachmentRemoveCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AttachmentNotFound_Returns404()
    {
        var attachmentId = Guid.NewGuid();
        _attachmentsRepo.Setup(r => r.GetByIdAsync(attachmentId)).ReturnsAsync((TicketAttachment?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new ChatAttachmentRemoveCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            AttachmentId = attachmentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
