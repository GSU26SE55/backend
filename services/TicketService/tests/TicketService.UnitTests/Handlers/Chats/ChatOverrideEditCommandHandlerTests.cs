using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatOverrideEditCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<IMarkdownRenderer> _markdownRenderer = new();

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

        var chatEditsRepo = new Mock<IGenericRepository<TicketChatEdit>>();
        uow.SetupGet(u => u.TicketChatEdits).Returns(chatEditsRepo.Object);

        var handler = new ChatOverrideEditCommandHandler(uow.Object, _activityLogger.Object, _markdownRenderer.Object);
        var command = new ChatOverrideEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "Edited via override",
            OverrideReason = "Data correction"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chat.Body.Should().Be("Edited via override");
        chatEditsRepo.Verify(r => r.AddAsync(It.Is<TicketChatEdit>(e => e.EditReason == "Data correction")), Times.Once);
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

        var handler = new ChatOverrideEditCommandHandler(uow.Object, _activityLogger.Object, _markdownRenderer.Object);
        var command = new ChatOverrideEditCommand
        {
            TicketId = ticketId,
            ChatId = chatId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            Body = "Trying override",
            OverrideReason = "Reason"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chat.Body.Should().Be("Original body");
    }

    [Fact]
    public async Task Validate_MissingOverrideReason_ReturnsError()
    {
        var command = new ChatOverrideEditCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "Edited",
            OverrideReason = ""
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "OverrideReason");
    }
}
