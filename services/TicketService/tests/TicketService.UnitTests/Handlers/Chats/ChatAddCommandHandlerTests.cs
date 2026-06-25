using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAdd;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatAddCommandHandlerTests
{
    private IReadOnlyList<string> NoMatches = Array.Empty<string>();
    private static readonly List<string> PublicCreatePermission = new() { ChatPermissionCodes.ChatCreatePublic };
    private static readonly List<string> InternalCreatePermission = new() { ChatPermissionCodes.ChatCreateInternal };

    private readonly Mock<IActivityLogger> _activityLogger = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _realtimeNotifier = new();
    private readonly Mock<IMarkdownRenderer> _markdownRenderer = new();
    private readonly Mock<ISpamDetector> _spamDetector = new();
    private readonly Mock<IProfanityFilter> _profanityFilter = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly Mock<ILogger<ChatAddCommandHandler>> _loggerMock = new();
    private readonly IOptions<ChatOptions> _chatOptions = Options.Create(new ChatOptions());

    public ChatAddCommandHandlerTests()
    {
        _spamDetector.Setup(x => x.IsSpamAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _profanityFilter.Setup(x => x.ContainsProfanity(It.IsAny<string>(), out NoMatches)).Returns(false);
        _piiDetector.Setup(x => x.ContainsPii(It.IsAny<string>(), out NoMatches)).Returns(false);
    }

    private ChatAddCommandHandler CreateHandler(Mock<ITicketUnitOfWork> uow) =>
        new(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object,
            new ChatAuthorizationService(uow.Object), _spamDetector.Object, _profanityFilter.Object, _piiDetector.Object,
            _chatOptions, _loggerMock.Object);

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

        var (uow, _, _, _, _, _, _, chats, attachments, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
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
            UserPermissions = PublicCreatePermission,
            Attachments = new List<ChatAttachmentInput>
            {
                new ChatAttachmentInput(Guid.NewGuid(), "file.pdf", "application/pdf", 1024)
            }
        };

        var handler = CreateHandler(uow);

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
    public async Task Handle_MarkdownBody_RendersBodyHtml()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        _markdownRenderer.Setup(r => r.RenderToHtml("**bold**", It.IsAny<IEnumerable<Guid>>())).Returns("<p><strong>bold</strong></p>");

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "**bold**",
            BodyFormat = ChatBodyFormatEnum.Markdown,
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c =>
            c.BodyFormat == ChatBodyFormatEnum.Markdown &&
            c.BodyHtml == "<p><strong>bold</strong></p>")), Times.Once);
    }

    [Fact]
    public async Task Handle_TicketClosed_ReturnsBadRequest()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description",
            Status = TicketStatusEnum.Closed
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "This is a comment",
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ClosedPendingRate_CustomerAllowedToAdd()
    {
        // #517 — Customer được miễn block khi ticket ClosedPendingRate (để feedback/rating).
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description",
            Status = TicketStatusEnum.ClosedPendingRate
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            Body = "Feedback comment",
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClosedPendingRate_StaffBlocked()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description",
            Status = TicketStatusEnum.ClosedPendingRate
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Trying to add",
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InternalChatWithoutPermission_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            Body = "Trying internal chat",
            IsInternal = true,
            UserPermissions = PublicCreatePermission // có public nhưng KHÔNG có internal
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);
    }

    [Fact]
    public async Task Handle_InternalChatWithPermission_Succeeds()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Internal note",
            IsInternal = true,
            UserPermissions = InternalCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c => c.IsInternal)), Times.Once);
    }

    [Fact]
    public async Task Handle_SpamDetected_ReturnsBadRequestAndLogsFlagged()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        _spamDetector.Setup(x => x.IsSpamAsync(ticketId, userId, "Same message", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            Body = "Same message",
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, userId, ActorRoleEnum.Customer, "Customer",
            ActivityActionEnum.ChatFlagged, null, null, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProfanityAndPiiDetected_ReturnsWarningsAndLogsFlagged()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        IReadOnlyList<string> profanityMatches = new List<string> { "ngu" };
        IReadOnlyList<string> piiMatches = new List<string> { "Email" };
        _profanityFilter.Setup(x => x.ContainsProfanity("đồ ngu, email a@b.com", out profanityMatches)).Returns(true);
        _piiDetector.Setup(x => x.ContainsPii("đồ ngu, email a@b.com", out piiMatches)).Returns(true);

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            Body = "đồ ngu, email a@b.com",
            UserPermissions = PublicCreatePermission
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Warnings.Should().NotBeNull();
        result.Data!.Warnings!.Count.Should().Be(2);

        _activityLogger.Verify(x => x.LogAsync(
            ticketId, userId, ActorRoleEnum.Customer, "Customer",
            ActivityActionEnum.ChatFlagged, null, null, It.IsAny<string>()), Times.Once);
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

    [Fact]
    public async Task Validate_EmojiOnlyBody_ReturnsError()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "User",
            Body = "☀☁☂", // BMP weather symbols (U+2600-2602) — nằm trong heuristic emoji range
            IsInternal = false
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }

    [Fact]
    public async Task Validate_BodyTooLong_ReturnsError()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "User",
            Body = new string('a', 10001)
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Body");
    }
}
