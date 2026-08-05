using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using SharedInfrastructure.Metrics;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAddCommandHandler : IRequestHandler<ChatAddCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ISpamDetector _spamDetector;
    private readonly IProfanityFilter _profanityFilter;
    private readonly IPiiDetector _piiDetector;
    private readonly ChatOptions _chatOptions;
    private readonly ILogger<ChatAddCommandHandler> _logger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IGroupMentionResolverService _groupMentionResolver;
    private readonly IPublisher _publisher;   // Sprint Chat DoD — audit chat.create/mention
    private readonly IChatCacheService _chatCache;
    private readonly ISlaService _slaService;
    private readonly IChatRecipientResolver _recipientResolver;


    public ChatAddCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IMarkdownRenderer markdownRenderer,
        IChatAuthorizationService chatAuthorizationService,
        ISpamDetector spamDetector,
        IProfanityFilter profanityFilter,
        IPiiDetector piiDetector,
        IOptions<ChatOptions> chatOptions,
        ILogger<ChatAddCommandHandler> logger,
        IIntegrationEventOutboxWriter outboxWriter,
        IGroupMentionResolverService groupMentionResolver,
        IChatCacheService chatCache,
        ISlaService slaService,
        IChatRecipientResolver recipientResolver,
        IPublisher publisher)
    {
        _publisher = publisher;
        _uow = uow;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _markdownRenderer = markdownRenderer;
        _chatAuthorizationService = chatAuthorizationService;
        _spamDetector = spamDetector;
        _profanityFilter = profanityFilter;
        _piiDetector = piiDetector;
        _chatOptions = chatOptions.Value;
        _logger = logger;
        _outboxWriter = outboxWriter;
        _groupMentionResolver = groupMentionResolver;
        _chatCache = chatCache;
        _slaService = slaService;
        _recipientResolver = recipientResolver;
    }

    public async Task<TicketActionResponse> Handle(ChatAddCommand request, CancellationToken cancellationToken)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        // Ticket.PrimaryHandlerStaffId là [NotMapped] — nguồn sự thật nằm ở ticket_assignments.
        // Không có dòng PrimaryHandler thì đây là null, và ChatCreatedEvent.AssignedStaffId cũng null;
        // người nhận thông báo lấy từ IChatRecipientResolver chứ không dựa vào trường này.
        ticket.PrimaryHandlerStaffId = await _uow.TicketAssignments.GetAllAsync()
            .Where(a => a.TicketId == ticket.Id && !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
            .Select(a => (Guid?)a.StaffId)
            .FirstOrDefaultAsync(cancellationToken);

        var blockReason = ChatClosedStateHelper.GetBlockReason(
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Add, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        if (!_chatAuthorizationService.CanCreateChat(request.IsInternal, request.UserPermissions))
            return Fail(403, request.IsInternal ? "Không có quyền tạo tin nhắn nội bộ." : "Không có quyền tạo tin nhắn.");

        var spamLease = await _spamDetector.TryAcquireLeaseAsync(request.TicketId, request.UserId, cancellationToken);
        if (spamLease is null)
            return Fail(409, "CHAT_SPAM_CHECK_IN_PROGRESS");

        using var leaseRenewalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseRenewalTask = RenewSpamLeaseUntilCancelledAsync(spamLease, leaseRenewalCts.Token);

        try
        {
            if (await _spamDetector.IsSpamAsync(request.TicketId, request.UserId, request.Body, cancellationToken))
                return Fail(400, "CHAT_DUPLICATE_MESSAGE_LIMIT");

            var warnings = new List<string>();
            if (_profanityFilter.ContainsProfanity(request.Body, out var profanityMatches))
                warnings.Add($"Nội dung có thể chứa từ ngữ không phù hợp: {string.Join(", ", profanityMatches)}.");
            if (_piiDetector.ContainsPii(request.Body, out var piiMatches))
                warnings.Add($"Nội dung có thể chứa thông tin cá nhân: {string.Join(", ", piiMatches)}.");

            List<TicketParticipant> activeParticipants = new();
            bool needParticipants = (request.Mentions != null && request.Mentions.Any())
                                 || (request.GroupMentions != null && request.GroupMentions.Any());

            if (needParticipants)
            {
                activeParticipants = await _uow.TicketParticipants.GetAllAsync()
                    .AsNoTracking()
                    .Where(p => p.TicketId == ticket.Id && p.RemovedAt == null && !p.IsDeleted)
                    .ToListAsync(cancellationToken);
            }

            if (request.Mentions != null && request.Mentions.Any())
            {
                for (int i = 0; i < request.Mentions.Count; i++)
                {
                    var mentionInput = request.Mentions[i];
                    if (!activeParticipants.Any(p => p.UserId == mentionInput.UserId))
                    {
                        var response = new TicketActionResponse
                        {
                            IsSuccess = false,
                            StatusCode = 400,
                            Message = "Dữ liệu đầu vào không hợp lệ."
                        };
                        response.ListErrors.Add(new Errors
                        {
                            Field = $"Mentions[{i}].UserId",
                            Detail = "User được mention phải là participant active của ticket."
                        });
                        return response;
                    }
                }
            }

            // Resolve group mentions → individual users (dedup với individual mentions)
            var resolvedGroupUsers = new List<(Guid UserId, ActorRoleEnum Role, string? DisplayName)>();
            if (request.GroupMentions != null && request.GroupMentions.Any())
            {
                var seenUserIds = new HashSet<Guid>(
                    request.Mentions?.Select(m => m.UserId) ?? Enumerable.Empty<Guid>());

                foreach (var gm in request.GroupMentions)
                {
                    var resolved = await _groupMentionResolver.ResolveAsync(gm, activeParticipants, cancellationToken);
                    foreach (var user in resolved)
                    {
                        if (seenUserIds.Add(user.UserId))
                            resolvedGroupUsers.Add(user);
                    }
                }
            }

            var chat = new TicketChat
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                AuthorUserId = request.UserId,
                AuthorRole = request.UserRole,
                AuthorDisplayName = request.UserDisplayName,
                Body = request.Body,
                IsInternal = request.IsInternal,
                BodyFormat = request.BodyFormat,
                AttachmentFileIds = request.Attachments?.Select(a => a.FileId).ToList() ?? new List<Guid>()
            };

            if (chat.BodyFormat == ChatBodyFormatEnum.Markdown)
                chat.BodyHtml = _markdownRenderer.RenderToHtml(chat.Body, chat.AttachmentFileIds);

            await _uow.TicketChats.AddAsync(chat);

            if (request.Attachments != null && request.Attachments.Any())
            {
                foreach (var att in request.Attachments)
                {
                    var attachment = new TicketAttachment
                    {
                        Id = Guid.NewGuid(),
                        TicketId = ticket.Id,
                        Ticket = ticket,
                        UploadedByUserId = request.UserId,
                        FileId = att.FileId,
                        FileName = att.FileName,
                        ContentType = att.ContentType,
                        SizeBytes = att.SizeBytes,
                        Source = request.UserRole == ActorRoleEnum.Customer
                            ? AttachmentSourceEnum.CustomerSubmission
                            : AttachmentSourceEnum.StaffWork,
                        Url = att.Url
                    };
                    await _uow.TicketAttachments.AddAsync(attachment);
                }
            }

            var createdMentions = new List<TicketChatMention>();

            // Individual mentions
            if (request.Mentions != null && request.Mentions.Any())
            {
                foreach (var mentionInput in request.Mentions)
                {
                    var participant = activeParticipants.First(p => p.UserId == mentionInput.UserId);
                    var mention = new TicketChatMention
                    {
                        Id = Guid.NewGuid(),
                        ChatId = chat.Id,
                        Chat = chat,
                        MentionedUserId = mentionInput.UserId,
                        MentionedUserRole = participant.UserRole,
                        MentionedDisplayName = mentionInput.DisplayName,
                    };
                    await _uow.TicketChatMentions.AddAsync(mention);
                    createdMentions.Add(mention);

                    await _outboxWriter.WriteAsync(new ChatMentionedEvent(
                        chat.Id,
                        chat.TicketId,
                        mentionInput.UserId,
                        (int)participant.UserRole,
                        mentionInput.DisplayName,
                        request.UserId,
                        false), cancellationToken);
                }
            }

            // Group mentions (already deduped against individual mentions)
            foreach (var (userId, role, displayName) in resolvedGroupUsers)
            {
                var mention = new TicketChatMention
                {
                    Id = Guid.NewGuid(),
                    ChatId = chat.Id,
                    Chat = chat,
                    MentionedUserId = userId,
                    MentionedUserRole = role,
                    MentionedDisplayName = displayName,
                };
                await _uow.TicketChatMentions.AddAsync(mention);
                createdMentions.Add(mention);

                await _outboxWriter.WriteAsync(new ChatMentionedEvent(
                    chat.Id,
                    chat.TicketId,
                    userId,
                    (int)role,
                    displayName ?? string.Empty,
                    request.UserId,
                    true), cancellationToken);
            }

            // #572 — Metrics
            AppMetrics.ChatEventsTotal.WithLabels(ticket.Id.ToString(), "add").Inc();
            foreach (var m in createdMentions)
                AppMetrics.ChatMentionCountTotal.WithLabels(m.MentionedUserRole.ToString()).Inc();

            // #566 — Trigger escalation saga nếu Manager được mention trên ticket P1 Critical
            if (ticket.Priority == TicketPriorityEnum.P1Critical && createdMentions.Count > 0)
            {
                var managerMention = createdMentions.FirstOrDefault(m => m.MentionedUserRole == ActorRoleEnum.Manager);
                if (managerMention != null)
                {
                    await _outboxWriter.WriteAsync(new ChatEscalationReviewRequestedEvent(
                        chat.Id,
                        ticket.Id,
                        ticket.Code,
                        managerMention.MentionedUserId,
                        $"Manager được mention trên ticket P1 Critical #{ticket.Code}."), cancellationToken);
                }
            }

            await _activityLogger.LogAsync(
                ticket.Id,
                request.UserId,
                request.UserRole,
                request.UserDisplayName,
                ActivityActionEnum.Chatted,
                null,
                request.IsInternal ? "[Nội bộ]" : "[Công khai]",
                $"Đã thêm tin nhắn chat: {request.Body[..Math.Min(request.Body.Length, 50)]}...");

            if (warnings.Count > 0)
            {
                await _activityLogger.LogAsync(
                    ticket.Id, request.UserId, request.UserRole, request.UserDisplayName,
                    ActivityActionEnum.ChatFlagged, null, null, string.Join(" | ", warnings));
            }

            var recipientIds = await _recipientResolver.ResolveAsync(
                ticket.Id, ticket.CustomerId, chat.AuthorUserId, chat.IsInternal, cancellationToken);

            await _outboxWriter.WriteAsync(new ChatCreatedEvent(
                chat.Id,
                chat.TicketId,
                chat.AuthorUserId,
                (int)chat.AuthorRole,
                chat.AuthorDisplayName,
                chat.Body,
                chat.IsInternal,
                chat.AttachmentFileIds,
                ticket.CustomerId,
                ticket.PrimaryHandlerStaffId,
                recipientIds), cancellationToken);

            if (request.RequestCustomerInfo &&
                request.UserRole is ActorRoleEnum.Staff or ActorRoleEnum.Manager or ActorRoleEnum.Admin)
            {
                await _slaService.PauseForCustomerInfoAsync(ticket.Id, chat.Id, request.UserId, cancellationToken);
            }

            if (request.UserRole == ActorRoleEnum.Customer)
            {
                await _slaService.ResumeOnCustomerReplyAsync(ticket.Id, request.UserId, cancellationToken);
            }

            await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
                TicketService.Domain.Enums.TicketAuditActionEnum.ChatCreated, ticket.Id, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id, ["isInternal"] = chat.IsInternal }), cancellationToken);

            if (createdMentions.Count > 0)
            {
                await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
                    TicketService.Domain.Enums.TicketAuditActionEnum.ChatMentioned, ticket.Id, targetDisplay: ticket.Code,
                    metadata: new Dictionary<string, object?>
                    {
                        ["chatId"] = chat.Id,
                        ["mentionedUserIds"] = createdMentions.Select(m => m.MentionedUserId).ToArray()
                    }), cancellationToken);
            }

            await _uow.SaveChangesAsync(cancellationToken);

            // Invalidate cache page 1 (#552)
            await _spamDetector.RecordAcceptedMessageAsync(request.TicketId, request.UserId, request.Body, cancellationToken);
            await _chatCache.InvalidateAsync(ticket.Id, cancellationToken);

            // Broadcast chat via SignalR
            try
            {
                var chatDto = new TicketChatDTO
                {
                    Id = chat.Id.ToString(),
                    TicketId = chat.TicketId.ToString(),
                    AuthorUserId = chat.AuthorUserId.ToString(),
                    AuthorRole = chat.AuthorRole,
                    AuthorDisplayName = chat.AuthorDisplayName,
                    Body = chat.Body,
                    IsInternal = chat.IsInternal,
                    AttachmentFileIds = chat.AttachmentFileIds.Select(id => id.ToString()).ToList(),
                    CreatedAt = chat.CreatedAt,
                    Mentions = createdMentions.Select(m => new TicketChatMentionDTO
                    {
                        Id = m.Id.ToString(),
                        ChatId = m.ChatId.ToString(),
                        MentionedUserId = m.MentionedUserId.ToString(),
                        MentionedUserRole = m.MentionedUserRole,
                        MentionedDisplayName = m.MentionedDisplayName,
                        IsInternal = chat.IsInternal,
                        CreatedAt = m.CreatedAt
                    }).ToList()
                };
                await _realtimeNotifier.NotifyChatAddedAsync(chatDto, cancellationToken);

                foreach (var mention in createdMentions)
                {
                    if (mention.MentionedUserId != request.UserId)
                        await _realtimeNotifier.NotifyMentionReceivedAsync(mention.MentionedUserId, chatDto, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatAdd] Failed to broadcast ChatAdded SignalR event for ticket {TicketId}", ticket.Id);
            }

            return new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 201,
                Message = "Thêm tin nhắn chat thành công.",
                Data = new TicketActionDTO
                {
                    Id = chat.Id.ToString(),
                    TicketId = ticket.Id.ToString(),
                    Code = ticket.Code,
                    Status = ticket.Status,
                    Warnings = warnings.Count > 0 ? warnings : null
                }
            };
        }
        finally
        {
            leaseRenewalCts.Cancel();
            await AwaitLeaseRenewalAsync(leaseRenewalTask);
            await _spamDetector.ReleaseLeaseAsync(spamLease, CancellationToken.None);
        }
    }

    private async Task RenewSpamLeaseUntilCancelledAsync(SpamLease lease, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!await _spamDetector.RenewLeaseAsync(lease, cancellationToken))
                {
                    _logger.LogWarning("[ChatAdd] Lost spam lease for {LeaseKey}.", lease.Key);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Request completed normally.
        }
    }

    private static async Task AwaitLeaseRenewalAsync(Task renewalTask)
    {
        try
        {
            await renewalTask;
        }
        catch (OperationCanceledException)
        {
            // Expected while releasing the request lease.
        }
    }

    private static TicketActionResponse Fail(int statusCode, string message)
    {
        return new TicketActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
