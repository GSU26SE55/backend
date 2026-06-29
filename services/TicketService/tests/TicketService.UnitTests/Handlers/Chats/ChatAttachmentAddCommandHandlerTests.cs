using Microsoft.Extensions.Options;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatAttachmentAddCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly IOptions<ChatOptions> _chatOptions = Options.Create(new ChatOptions());

    private ChatAttachmentAddCommandHandler CreateHandler(List<TicketAttachment>? existingAttachments = null)
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.TicketAttachments).Returns(_attachmentsRepo.Object);
        _attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketAttachment>(existingAttachments ?? new List<TicketAttachment>()));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ChatAttachmentAddCommandHandler(_uow.Object, _chatOptions);
    }

    private static Ticket MakeTicket(Guid id, TicketStatusEnum status = TicketStatusEnum.InProgress) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test Ticket",
        Description = "desc",
        Status = status
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

    [Fact]
    public async Task Handle_AuthorAddsValidAttachment_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            FileId = Guid.NewGuid(),
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        _attachmentsRepo.Verify(r => r.AddAsync(It.Is<TicketAttachment>(a => a.FileName == "photo.jpg")), Times.Once);
    }

    [Fact]
    public async Task Handle_OverSizeLimit_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            FileId = Guid.NewGuid(),
            FileName = "huge.mp4",
            ContentType = "video/mp4",
            SizeBytes = 100 * 1024 * 1024 // 100MB > 50MB limit
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_DisallowedMimeType_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            FileId = Guid.NewGuid(),
            FileName = "script.exe",
            ContentType = "application/x-msdownload",
            SizeBytes = 1024
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_MaxAttachmentsReached_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var existing = Enumerable.Range(0, 10).Select(_ => new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ChatId = chatId,
            UploadedByUserId = authorId,
            FileId = Guid.NewGuid(),
            FileName = "f.png",
            ContentType = "image/png",
            SizeBytes = 100,
            Ticket = ticket
        }).ToList();

        var handler = CreateHandler(existing);
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            FileId = Guid.NewGuid(),
            FileName = "one-more.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NonAuthorNonManager_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = otherStaffId,
            UserRole = ActorRoleEnum.Staff,
            FileId = Guid.NewGuid(),
            FileName = "file.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.Closed);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatAttachmentAddCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Customer,
            FileId = Guid.NewGuid(),
            FileName = "file.png",
            ContentType = "image/png",
            SizeBytes = 1024
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Validate_MissingFileName_ReturnsError()
    {
        var command = new ChatAttachmentAddCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            FileId = Guid.NewGuid(),
            FileName = "",
            ContentType = "image/png",
            SizeBytes = 100
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "FileName");
    }
}
