using Microsoft.Extensions.Logging;
using Moq;
using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatAddCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<ILogger<ChatAddCommandHandler>> _loggerMock = new();

    [Fact]
    public async Task Handle_ValidRequest_AddsChat()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, attachments, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "This is a comment",
            IsInternal = false,
            Attachments = new List<ChatAttachmentInput>
            {
                new ChatAttachmentInput(Guid.NewGuid(), "file.pdf", "application/pdf", 1024)
            }
        };

        var handler = new ChatAddCommandHandler(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _loggerMock.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c =>
            c.TicketId == ticketId &&
            c.Body == "This is a comment" &&
            c.AttachmentFileIds.Count == 1)), Times.Once);

        attachments.Verify(x => x.AddAsync(It.Is<TicketAttachment>(a =>
            a.TicketId == ticketId &&
            a.FileName == "file.pdf")), Times.Once);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId,
            userId,
            ActorRoleEnum.Staff,
            "Staff User",
            ActivityActionEnum.Chatted,
            null,
            "[Công khai]",
            It.IsAny<string>()), Times.Once);

        _realtimeNotifier.Verify(x => x.NotifyChatAddedAsync(
            It.Is<TicketChatDTO>(dto => dto.TicketId == ticketId.ToString() && dto.Body == "This is a comment"),
            It.IsAny<CancellationToken>()), Times.Once);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Validate_EmptyBody_ReturnsError()
    {
        // Arrange
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "User",
            Body = "",
            IsInternal = false
        };

        // Act
        var result = await command.ValidateAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }
}
