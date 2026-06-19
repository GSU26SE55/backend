using TicketService.Application.CQRS.Command.CommentAdd;
using TicketService.Application.CQRS.Handler.Comments;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Comments;

public class CommentAddCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _logger = new();

    [Fact]
    public async Task Handle_ValidRequest_AddsComment()
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

        var (uow, _, _, _, _, _, _, comments, attachments, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new CommentAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "This is a comment",
            IsInternal = false,
            Attachments = new List<CommentAttachmentInput>
            {
                new CommentAttachmentInput(Guid.NewGuid(), "file.pdf", "application/pdf", 1024)
            }
        };

        var handler = new CommentAddCommandHandler(uow.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        comments.Verify(x => x.AddAsync(It.Is<TicketComment>(c =>
            c.TicketId == ticketId &&
            c.Body == "This is a comment" &&
            c.AttachmentFileIds.Count == 1)), Times.Once);

        attachments.Verify(x => x.AddAsync(It.Is<TicketAttachment>(a =>
            a.TicketId == ticketId &&
            a.FileName == "file.pdf")), Times.Once);

        _logger.Verify(x => x.LogAsync(
            ticketId,
            userId,
            ActorRoleEnum.Staff,
            "Staff User",
            ActivityActionEnum.Commented,
            null,
            "[Công khai]",
            It.IsAny<string>()), Times.Once);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Validate_EmptyBody_ReturnsError()
    {
        // Arrange
        var command = new CommentAddCommand
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
