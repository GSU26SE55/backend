using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatAddCommandHandlerTests
{
    // Sprint Chat DoD — bắt notification audit để khẳng định handler THỰC SỰ ghi vết.
    private readonly Mock<IPublisher> _publisher = new();
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
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<IGroupMentionResolverService> _groupMentionResolver = new();

    private readonly Mock<IChatCacheService> _chatCache = new();
    private readonly Mock<ISlaService> _slaService = new();
    private readonly Mock<ITicketStateMachine> _stateMachine = new();
    private readonly Mock<IChatRecipientResolver> _recipientResolver = new();

    public ChatAddCommandHandlerTests()
    {
        _spamDetector.Setup(x => x.TryAcquireLeaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpamLease("test-spam-lease", "test-owner"));
        _spamDetector.Setup(x => x.RenewLeaseAsync(It.IsAny<SpamLease>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _spamDetector.Setup(x => x.ReleaseLeaseAsync(It.IsAny<SpamLease>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _spamDetector.Setup(x => x.RecordAcceptedMessageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _spamDetector.Setup(x => x.IsSpamAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _profanityFilter.Setup(x => x.ContainsProfanity(It.IsAny<string>(), out NoMatches)).Returns(false);
        _piiDetector.Setup(x => x.ContainsPii(It.IsAny<string>(), out NoMatches)).Returns(false);
        _groupMentionResolver
            .Setup(x => x.ResolveAsync(It.IsAny<GroupMentionInput>(), It.IsAny<List<TicketParticipant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, ActorRoleEnum, string?)>());
        _recipientResolver
            .Setup(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
    }

    private ChatAddCommandHandler CreateHandler(Mock<ITicketUnitOfWork> uow) =>
        new(uow.Object, _activityLogger.Object, _realtimeNotifier.Object, _markdownRenderer.Object,
            new ChatAuthorizationService(uow.Object), _spamDetector.Object, _profanityFilter.Object, _piiDetector.Object,
            _chatOptions, _loggerMock.Object, _outboxWriter.Object, _groupMentionResolver.Object, _chatCache.Object,
            _slaService.Object, _stateMachine.Object, _recipientResolver.Object, _publisher.Object);

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

        // Sprint Chat DoD — chat.created phải sinh audit trail.
        _publisher.Verify(x => x.Publish(
            It.Is<TicketAuditTrailNotification>(nn => nn.ActionCode == nameof(TicketAuditActionEnum.ChatCreated)),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Handle_SpamDetected_ReturnsStableDuplicateErrorWithoutRecordingAttempt()
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
            ActivityActionEnum.ChatFlagged, null, null, It.IsAny<string>()), Times.Never);
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

    [Fact]
    public async Task Handle_MentionOfActiveParticipant_CreatesMentionAndPublishesEvent()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var mentionedUserId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };
        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = mentionedUserId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.Collaborator,
            CanPost = true,
            CanViewInternal = true,
            AddedByUserId = userId,
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            participantSeed: new[] { participant }
        );
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention>());

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Cần xác nhận",
            UserPermissions = PublicCreatePermission,
            Mentions = new List<ChatMentionInput> { new(mentionedUserId, "Staff B") }
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        mentionsRepo.Verify(r => r.AddAsync(It.Is<TicketChatMention>(m =>
            m.MentionedUserId == mentionedUserId &&
            m.MentionedUserRole == ActorRoleEnum.Staff &&
            m.MentionedDisplayName == "Staff B")), Times.Once);

        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<ChatMentionedEvent>(e => e.MentionedUserId == mentionedUserId && e.ActorUserId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MentionOfNonParticipant_ReturnsValidationError()
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
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention>());

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff User",
            Body = "Cần xác nhận",
            UserPermissions = PublicCreatePermission,
            Mentions = new List<ChatMentionInput> { new(Guid.NewGuid(), "Không phải participant") }
        };

        var handler = CreateHandler(uow);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(e => e.Field == "Mentions[0].UserId");
        chats.Verify(x => x.AddAsync(It.IsAny<TicketChat>()), Times.Never);
        mentionsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChatMention>()), Times.Never);
    }

    // ─── Group mention tests (#537) ───────────────────────────────────────────

    [Fact]
    public async Task Handle_GroupMentionRole_CreatesGroupMentionRows()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention>());

        _groupMentionResolver
            .Setup(x => x.ResolveAsync(
                It.Is<GroupMentionInput>(g => g.GroupType == "role" && g.GroupIdentifier == "manager"),
                It.IsAny<List<TicketParticipant>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, ActorRoleEnum, string?)>
            {
                (managerId, ActorRoleEnum.Manager, "Manager A")
            });

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Cc @role:manager",
            UserPermissions = PublicCreatePermission,
            GroupMentions = new List<GroupMentionInput> { new("role", "manager") }
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        mentionsRepo.Verify(r => r.AddAsync(It.Is<TicketChatMention>(m =>
            m.MentionedUserId == managerId &&
            m.MentionedUserRole == ActorRoleEnum.Manager &&
            m.MentionedDisplayName == "Manager A")), Times.Once);

        _outboxWriter.Verify(x => x.WriteAsync(
            It.Is<ChatMentionedEvent>(e =>
                e.MentionedUserId == managerId &&
                e.IsGroupMention == true &&
                e.ActorUserId == authorId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_GroupMentionResolves0Users_ChatStillSucceeds()
    {
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention>());

        // Resolver returns empty — no matching participants
        _groupMentionResolver
            .Setup(x => x.ResolveAsync(It.IsAny<GroupMentionInput>(), It.IsAny<List<TicketParticipant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, ActorRoleEnum, string?)>());

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Ping @team:tier3-staff",
            UserPermissions = PublicCreatePermission,
            GroupMentions = new List<GroupMentionInput> { new("team", "tier3-staff") }
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        mentionsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChatMention>()), Times.Never);
    }

    [Fact]
    public async Task Handle_GroupAndIndividualSameUser_DeduplicatesToOneRecord()
    {
        var ticketId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var sharedUserId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "T", Description = "D" };
        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = ticket,
            UserId = sharedUserId,
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.Collaborator,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = authorId,
            AddedAt = DateTime.UtcNow
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            participantSeed: new[] { participant });
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention>());

        // Group also resolves the same user
        _groupMentionResolver
            .Setup(x => x.ResolveAsync(It.IsAny<GroupMentionInput>(), It.IsAny<List<TicketParticipant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, ActorRoleEnum, string?)>
            {
                (sharedUserId, ActorRoleEnum.Staff, "Staff A")
            });

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = authorId,
            UserRole = ActorRoleEnum.Manager,
            UserDisplayName = "Manager",
            Body = "Hey",
            UserPermissions = PublicCreatePermission,
            Mentions = new List<ChatMentionInput> { new(sharedUserId, "Staff A") }, // individual
            GroupMentions = new List<GroupMentionInput> { new("role", "staff") }    // same user via group
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Only 1 mention record — individual wins (IsGroupMention=false)
        mentionsRepo.Verify(r => r.AddAsync(It.IsAny<TicketChatMention>()), Times.Once);
        mentionsRepo.Verify(r => r.AddAsync(It.Is<TicketChatMention>(m =>
            m.MentionedUserId == sharedUserId &&
            m.MentionedDisplayName == "Staff A")), Times.Once);
    }

    [Fact]
    public async Task Validate_InvalidGroupType_ReturnsError()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Test message",
            GroupMentions = new List<GroupMentionInput> { new("invalid_type", "manager") }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "GroupMentions[0].GroupType");
    }

    [Fact]
    public async Task Validate_InvalidRoleIdentifier_ReturnsError()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Test message",
            GroupMentions = new List<GroupMentionInput> { new("role", "superadmin") }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "GroupMentions[0].GroupIdentifier");
    }

    [Fact]
    public async Task Validate_InvalidTeamIdentifier_ReturnsError()
    {
        var command = new ChatAddCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Test message",
            GroupMentions = new List<GroupMentionInput> { new("team", "tier4-staff") }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "GroupMentions[0].GroupIdentifier");
    }

    // ─── SLA integration tests (#563) ────────────────────────────────────────

    [Fact]
    public async Task Handle_StaffWithRequestCustomerInfo_PausesSla()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Staff",
            Body = "Cần thêm thông tin từ khách hàng",
            RequestCustomerInfo = true,
            UserPermissions = PublicCreatePermission
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _slaService.Verify(s => s.PauseForCustomerInfoAsync(
            ticketId, It.IsAny<Guid>(), userId, It.IsAny<CancellationToken>()), Times.Once);
        _slaService.Verify(s => s.ResumeOnCustomerReplyAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CustomerReply_ResumesSla()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });

        var command = new ChatAddCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Customer,
            UserDisplayName = "Customer",
            Body = "Đây là thông tin bạn cần",
            UserPermissions = PublicCreatePermission
        };

        var result = await CreateHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _slaService.Verify(s => s.ResumeOnCustomerReplyAsync(
            ticketId, userId, It.IsAny<CancellationToken>()), Times.Once);
        _slaService.Verify(s => s.PauseForCustomerInfoAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
