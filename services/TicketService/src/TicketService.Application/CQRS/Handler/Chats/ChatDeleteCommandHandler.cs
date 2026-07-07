using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

public class ChatDeleteCommandHandler : IRequestHandler<ChatDeleteCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ChatOptions _chatOptions;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IChatCacheService _chatCache;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatDeleteCommandHandler> _logger;

    public ChatDeleteCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IChatAuthorizationService chatAuthorizationService,
        IOptions<ChatOptions> chatOptions,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IChatCacheService chatCache,
        ICacheService cache,
        ILogger<ChatDeleteCommandHandler> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
        _chatOptions = chatOptions.Value;
        _outboxWriter = outboxWriter;
        _realtimeNotifier = realtimeNotifier;
        _chatCache = chatCache;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatDeleteCommand request, CancellationToken ct)
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
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Delete, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        var authResult = _chatAuthorizationService.CanDeleteChat(
            chat,
            request.UserId,
            request.UserPermissions);

        if (authResult == ChatAuthorizationResult.Forbidden)
        {
            // Non-owner: hide for caller only instead of true delete
            var alreadyHidden = await _uow.TicketChatHides
                .AnyAsync(h => h.ChatId == chat.Id && h.UserId == request.UserId && !h.IsDeleted);
            if (!alreadyHidden)
            {
                await _uow.TicketChatHides.AddAsync(new TicketChatHide
                {
                    ChatId = chat.Id,
                    UserId = request.UserId
                });
                await _uow.SaveChangesAsync(ct);
            }
            return new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Đã ẩn bình luận.",
                Data = new TicketActionDTO
                {
                    Id = chat.Id.ToString(),
                    TicketId = ticket.Id.ToString(),
                    Code = ticket.Code,
                    Status = ticket.Status
                }
            };
        }

        var oldBody = chat.Body;

        chat.IsDeleted = true;
        chat.DeletedAt = DateTime.UtcNow;
        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatDeleted,
            ChatTextHelper.Truncate(oldBody),
            null,
            null);

        await _outboxWriter.WriteAsync(new ChatDeletedEvent(
            chat.Id,
            chat.TicketId,
            request.UserId,
            (int)request.UserRole), ct);

        // #633 — Soft-delete stale translations atomically with the chat delete save
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

        await _uow.SaveChangesAsync(ct);

        // Clear Redis only after DB is committed
        foreach (var t in translations)
            await _cache.RemoveAsync($"chat-translation:{chat.Id}:{t.TargetLanguage}", ct);

        await _chatCache.InvalidateAsync(ticket.Id, ct);

        try
        {
            await _realtimeNotifier.NotifyChatDeletedAsync(ticket.Id, chat.Id, request.UserDisplayName, chat.IsInternal, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatDelete] Failed to broadcast ChatDeleted SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa bình luận thành công.",
            Data = new TicketActionDTO
            {
                Id = chat.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
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
