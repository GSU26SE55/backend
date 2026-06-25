using Moq;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatReply;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatReplyCommandHandlerTests
{
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();

    private ChatReplyCommandHandler CreateHandler()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new ChatReplyCommandHandler(_uow.Object, _activityLogger.Object, _outboxWriter.Object);
    }

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description"
    };

    private static TicketChat MakeParent(Guid id, Guid ticketId, Ticket ticket, Guid? parentChatId = null, Guid? threadRootId = null, bool isDeleted = false) => new()
    {
        Id = id,
        TicketId = ticketId,
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Parent body",
        Ticket = ticket,
        ParentChatId = parentChatId,
        ThreadRootId = threadRootId,
        IsDeleted = isDeleted
    };

    [Fact]
    public async Task Handle_ValidReply_AddsReplyAndIncrementsParentReplyCount()
    {
        var ticketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var parent = MakeParent(parentId, ticketId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "This is a reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        parent.ReplyCount.Should().Be(1);

        _chatsRepo.Verify(r => r.AddAsync(It.Is<TicketChat>(c =>
            c.TicketId == ticketId &&
            c.ParentChatId == parentId &&
            c.ThreadRootId == parentId &&
            c.Body == "This is a reply")), Times.Once);

        _chatsRepo.Verify(r => r.UpdateAsync(It.Is<TicketChat>(c => c.Id == parentId && c.ReplyCount == 1)), Times.Once);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, userId, ActorRoleEnum.Staff, "Staff User",
            ActivityActionEnum.ChatReplied, null, It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyUsesParentThreadRoot_WhenParentAlreadyHasThreadRoot()
    {
        var ticketId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var parent = MakeParent(parentId, ticketId, ticket, threadRootId: rootId);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Reply"
        };

        await handler.Handle(command, CancellationToken.None);

        _chatsRepo.Verify(r => r.AddAsync(It.Is<TicketChat>(c => c.ThreadRootId == rootId)), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyToReply_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var grandParentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var parent = MakeParent(parentId, ticketId, ticket, parentChatId: grandParentId, threadRootId: grandParentId);

        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Nested reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _chatsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ParentNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync((TicketChat?)null);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ParentBelongsToDifferentTicket_ReturnsNotFound()
    {
        var actualTicketId = Guid.NewGuid();
        var otherTicketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(actualTicketId);
        var parent = MakeParent(parentId, actualTicketId, ticket);

        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = otherTicketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _chatsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeletedParent_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        var parent = MakeParent(parentId, ticketId, ticket, isDeleted: true);

        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        ticket.Status = TicketStatusEnum.Closed;
        var parent = MakeParent(parentId, ticketId, ticket);

        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        _chatsRepo.Setup(r => r.GetByIdAsync(parentId)).ReturnsAsync(parent);

        var handler = CreateHandler();
        var command = new ChatReplyCommand
        {
            TicketId = ticketId,
            ParentChatId = parentId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Reply"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _chatsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Validate_EmptyBody_ReturnsError()
    {
        var command = new ChatReplyCommand
        {
            TicketId = Guid.NewGuid(),
            ParentChatId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "User",
            Body = ""
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }
}
