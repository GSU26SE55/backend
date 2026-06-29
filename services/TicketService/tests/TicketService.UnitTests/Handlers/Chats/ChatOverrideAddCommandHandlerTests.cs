using Microsoft.Extensions.Logging;
using Moq;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatOverrideAddCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<IMarkdownRenderer> _markdownRenderer = new();
    private readonly Mock<ILogger<ChatOverrideAddCommandHandler>> _loggerMock = new();
    private readonly Mock<MediatR.IPublisher> _publisherMock = new();

    [Fact]
    public async Task Handle_AdminWithReason_SucceedsEvenWhenTicketClosed()
    {
        var ticketId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var handler = new ChatOverrideAddCommandHandler(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object, _loggerMock.Object, _publisherMock.Object);
        var command = new ChatOverrideAddCommand
        {
            TicketId = ticketId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "Override comment",
            OverrideReason = "Data correction sau khi đóng ticket"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c => c.TicketId == ticketId && c.Body == "Override comment")), Times.Once);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, adminId, ActorRoleEnum.Admin, "Admin",
            ActivityActionEnum.Chatted, null, It.IsAny<string>(), "Data correction sau khi đóng ticket"), Times.Once);
    }

    [Fact]
    public async Task Handle_WithAttachmentsAndMarkdown_PersistsAttachmentsAndRendersHtml()
    {
        var ticketId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test",
            Description = "Test",
            Status = TicketStatusEnum.Closed
        };

        var (uow, _, _, _, _, _, _, chats, attachments, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });

        _markdownRenderer.Setup(r => r.RenderToHtml("**override**", It.IsAny<IEnumerable<Guid>>())).Returns("<p><strong>override</strong></p>");

        var handler = new ChatOverrideAddCommandHandler(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object, _loggerMock.Object, _publisherMock.Object);
        var command = new ChatOverrideAddCommand
        {
            TicketId = ticketId,
            UserId = adminId,
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "**override**",
            BodyFormat = ChatBodyFormatEnum.Markdown,
            OverrideReason = "Data correction sau khi đóng ticket",
            Attachments = new List<ChatAttachmentInput>
            {
                new ChatAttachmentInput(Guid.NewGuid(), "file.pdf", "application/pdf", 1024)
            }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c => c.BodyHtml == "<p><strong>override</strong></p>")), Times.Once);
        attachments.Verify(x => x.AddAsync(It.Is<TicketAttachment>(a => a.TicketId == ticketId && a.FileName == "file.pdf")), Times.Once);
    }

    [Fact]
    public async Task Handle_NonAdminRole_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test", Description = "Test" };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var handler = new ChatOverrideAddCommandHandler(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object, _loggerMock.Object, _publisherMock.Object);
        var command = new ChatOverrideAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            Body = "Trying override",
            OverrideReason = "Some reason"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsNotFound()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var handler = new ChatOverrideAddCommandHandler(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object, _loggerMock.Object, _publisherMock.Object);
        var command = new ChatOverrideAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "Override comment",
            OverrideReason = "Reason"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Validate_MissingOverrideReason_ReturnsError()
    {
        var command = new ChatOverrideAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Admin,
            UserDisplayName = "Admin",
            Body = "Override comment",
            OverrideReason = ""
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "OverrideReason");
    }
}
