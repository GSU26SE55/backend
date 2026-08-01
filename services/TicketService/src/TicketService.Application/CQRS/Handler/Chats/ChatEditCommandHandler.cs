using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatEditCommandHandler : IRequestHandler<ChatEditCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly IProfanityFilter _profanityFilter;
    private readonly IPiiDetector _piiDetector;
    private readonly ChatOptions _chatOptions;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IChatCacheService _chatCache;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatEditCommandHandler> _logger;

    private readonly IPublisher _publisher;   // Sprint Chat DoD — audit chat.edit

    public ChatEditCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IMarkdownRenderer markdownRenderer,
        IChatAuthorizationService chatAuthorizationService,
        IProfanityFilter profanityFilter,
        IPiiDetector piiDetector,
        IOptions<ChatOptions> chatOptions,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IChatCacheService chatCache,
        ICacheService cache,
        ILogger<ChatEditCommandHandler> logger,
        IPublisher publisher)
    {
        _publisher = publisher;
        _uow = uow;
        _activityLogger = activityLogger;
        _markdownRenderer = markdownRenderer;
        _chatAuthorizationService = chatAuthorizationService;
        _profanityFilter = profanityFilter;
        _piiDetector = piiDetector;
        _chatOptions = chatOptions.Value;
        _outboxWriter = outboxWriter;
        _realtimeNotifier = realtimeNotifier;
        _chatCache = chatCache;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatEditCommand request, CancellationToken ct)
    {
        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Không tìm thấy bình luận.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var blockReason = ChatClosedStateHelper.GetBlockReason(
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Edit, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        var authResult = _chatAuthorizationService.CanEditChat(
            chat,
            request.UserId,
            request.UserPermissions,
            _chatOptions.EditWindowMinutes);

        if (authResult == ChatAuthorizationResult.EditWindowExpired)
            return Fail(403, $"Đã quá thời gian cho phép chỉnh sửa ({_chatOptions.EditWindowMinutes} phút).");

        if (authResult == ChatAuthorizationResult.Forbidden)
            return Fail(403, "Không có quyền sửa bình luận này.");

        var warnings = new List<string>();
        if (_profanityFilter.ContainsProfanity(request.Body, out var profanityMatches))
            warnings.Add($"Nội dung có thể chứa từ ngữ không phù hợp: {string.Join(", ", profanityMatches)}.");
        if (_piiDetector.ContainsPii(request.Body, out var piiMatches))
            warnings.Add($"Nội dung có thể chứa thông tin cá nhân: {string.Join(", ", piiMatches)}.");

        var oldBody = chat.Body;

        var chatEdit = new TicketChatEdit
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Chat = chat,
            OldBody = oldBody,
            NewBody = request.Body,
            EditedAt = DateTime.UtcNow,
            EditedByUserId = request.UserId,
            EditedByRole = request.UserRole,
            EditReason = null
        };
        await _uow.TicketChatEdits.AddAsync(chatEdit);

        chat.Body = request.Body;
        chat.EditedAt = DateTime.UtcNow;
        chat.EditCount += 1;
        chat.LastEditedByUserId = request.UserId;

        if (chat.BodyFormat == ChatBodyFormatEnum.Markdown)
            chat.BodyHtml = _markdownRenderer.RenderToHtml(chat.Body, chat.AttachmentFileIds);

        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatEdited,
            ChatTextHelper.Truncate(oldBody),
            ChatTextHelper.Truncate(request.Body),
            null);

        if (warnings.Count > 0)
        {
            await _activityLogger.LogAsync(
                ticket.Id, request.UserId, request.UserRole, request.UserDisplayName,
                ActivityActionEnum.ChatFlagged, null, null, string.Join(" | ", warnings));
        }

        await _outboxWriter.WriteAsync(new ChatEditedEvent(
            chat.Id,
            chat.TicketId,
            request.UserId,
            (int)request.UserRole,
            oldBody,
            request.Body,
            string.Empty), ct);

        // #633 — Soft-delete stale translations atomically with the chat edit save
        var translations = await _uow.TicketChatTranslations
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.ChatId == chat.Id)
            .ToListAsync(ct);

        foreach (var t in translations)
        {
            t.IsDeleted = true;
            t.DeletedAt = DateTime.UtcNow;
            _uow.TicketChatTranslations.UpdateAsync(t);
        }

        // Sprint Chat DoD — audit ChatEdited. Publish TRƯỚC SaveChanges để entry audit +
        // outbox đi cùng transaction với thay đổi nghiệp vụ (#AUDIT-25/26).
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketService.Domain.Enums.TicketAuditActionEnum.ChatEdited, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id }), ct);

        await _uow.SaveChangesAsync(ct);

        // Clear Redis only after DB is committed
        foreach (var t in translations)
            await _cache.RemoveAsync($"chat-translation:{chat.Id}:{t.TargetLanguage}", ct);

        await _chatCache.InvalidateAsync(ticket.Id, ct);

        try
        {
            await _realtimeNotifier.NotifyChatEditedAsync(new TicketChatDTO
            {
                Id = chat.Id.ToString(),
                TicketId = chat.TicketId.ToString(),
                AuthorUserId = chat.AuthorUserId.ToString(),
                AuthorRole = chat.AuthorRole,
                AuthorDisplayName = chat.AuthorDisplayName,
                Body = chat.Body,
                BodyHtml = chat.BodyHtml,
                BodyFormat = chat.BodyFormat,
                IsInternal = chat.IsInternal,
                EditedAt = chat.EditedAt,
                EditCount = chat.EditCount,
                CreatedAt = chat.CreatedAt
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatEdit] Failed to broadcast ChatEdited SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Sửa bình luận thành công.",
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
