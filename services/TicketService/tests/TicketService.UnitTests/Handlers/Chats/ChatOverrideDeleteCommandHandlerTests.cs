using Moq;
using TicketService.Application.CQRS.Command.ChatOverrideDelete;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatOverrideDeleteCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _activityLogger = new();

    [Fact]
    public async Task Handle_AdminWithReason_SucceedsEvenWhenTicketClosed()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed
        };
        var chat = new TicketChat
        {
            Id = chatId,
            TicketId = ticketId,
            Ticket = ticket,
            AuthorUserId = Guid.NewGuid(),
            AuthorRole = ActorRoleEnum.Customer,
            Body = "Original body"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { chat });
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatOverrideDeleteCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatOverrideDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            OverrideReason = "Policy violation"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chat.IsDeleted.Should().BeTrue();

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, adminId, ActorRoleEnum.Admin, "Admin",
            ActivityActionEnum.ChatDeleted, It.IsAny<string>(), null, "Policy violation"), Times.Once);
    }

    [Fact]
    public async Task Handle_NonAdminRole_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test", Description = "Test" };
        var chat = new TicketChat
        {
            Id = chatId,
            TicketId = ticketId,
            Ticket = ticket,
            AuthorUserId = Guid.NewGuid(),
            AuthorRole = ActorRoleEnum.Customer,
            Body = "Original body"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { chat });
        chats.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync(chat);

        var handler = new ChatOverrideDeleteCommandHandler(uow.Object, _activityLogger.Object);
        var command = new ChatOverrideDeleteCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            OverrideReason = "Reason"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_MissingOverrideReason_ReturnsError()
    {
        var command = new ChatOverrideDeleteCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            OverrideReason = ""
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "OverrideReason");
    }
}
