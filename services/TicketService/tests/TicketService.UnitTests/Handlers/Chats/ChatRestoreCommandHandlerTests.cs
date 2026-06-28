using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatRestoreCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();

    private ChatRestoreCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ChatRestoreCommandHandler(_uow.Object, _activityLogger.Object);
    }

    private static Ticket MakeTicket(Guid id, TicketStatusEnum status = TicketStatusEnum.InProgress) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description",
        Status = status
    };

    private static TicketChat MakeChat(Guid id, Guid ticketId, Guid authorId, Ticket ticket, bool isDeleted = true) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Original body",
        Ticket = ticket,
        IsDeleted = isDeleted,
        DeletedAt = isDeleted ? DateTime.UtcNow : null
    };

    [Fact]
    public async Task Handle_AdminRestoreDeletedChat_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        chat.IsDeleted.Should().BeFalse();
        chat.DeletedAt.Should().BeNull();

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, adminId, ActorRoleEnum.Admin, "Admin",
            ActivityActionEnum.ChatRestored, null, It.IsAny<string>(), null), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TicketClosed_StillSucceeds()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, TicketStatusEnum.Closed);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChatNotDeleted_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket, isDeleted: false);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var chat = MakeChat(chatId, ticketId, authorId, ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync((Ticket?)null);

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ChatBelongsToDifferentTicket_ReturnsNotFound()
    {
        var actualTicketId = Guid.NewGuid();
        var otherTicketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var ticket = MakeTicket(actualTicketId);
        var chat = MakeChat(chatId, actualTicketId, authorId, ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);
        _ticketsRepo.Setup(r => r.GetByIdAsync(otherTicketId)).ReturnsAsync(MakeTicket(otherTicketId));

        var handler = CreateHandler();
        var command = new ChatRestoreCommand
        {
            TicketId = otherTicketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        chat.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyIds_ReturnsErrors()
    {
        var command = new ChatRestoreCommand
        {
            TicketId = Guid.Empty,
            ChatId = Guid.Empty,
            UserId = Guid.Empty,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin"
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "TicketId");
        result.ListErrors.Should().Contain(e => e.Field == "ChatId");
        result.ListErrors.Should().Contain(e => e.Field == "UserId");
    }
}
