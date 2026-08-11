using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatBulkDeleteCommandHandler : IRequestHandler<ChatBulkDeleteCommand, ChatBulkDeleteResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ChatOptions _chatOptions;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly IChatCacheService _chatCache;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatBulkDeleteCommandHandler> _logger;

    public ChatBulkDeleteCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IOptions<ChatOptions> chatOptions,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        IChatCacheService chatCache,
        ICacheService cache,
        ILogger<ChatBulkDeleteCommandHandler> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatOptions = chatOptions.Value;
        _outboxWriter = outboxWriter;
        _realtimeNotifier = realtimeNotifier;
        _chatCache = chatCache;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ChatBulkDeleteResponse> Handle(ChatBulkDeleteCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

        var blockReason = ChatClosedStateHelper.GetBlockReason(
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Delete, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        var chats = await _uow.TicketChats.GetAllAsync()
            .Where(x => !x.IsDeleted && x.TicketId == request.TicketId && request.ChatIds.Contains(x.Id))
            .ToListAsync(ct);

        var foundIds = chats.Select(x => x.Id).ToHashSet();
        var now = DateTime.UtcNow;
        var skippedIds = request.ChatIds
            .Where(id => !foundIds.Contains(id))
            .Select(id => id.ToString())
            .ToList();
        var deletedChats = new List<(Guid Id, bool IsInternal)>();
        var chatsToHide = new List<Guid>();

        foreach (var chat in chats)
        {
            if (chat.AuthorUserId != request.UserId)
            {
                // Non-owner: queue for per-user hide instead of skipping
                chatsToHide.Add(chat.Id);
                continue;
            }

            chat.IsDeleted = true;
            chat.DeletedAt = now;
            _uow.TicketChats.UpdateAsync(chat);
            deletedChats.Add((chat.Id, chat.IsInternal));
        }

        if (deletedChats.Count == 0 && chatsToHide.Count == 0)
        {
            return new ChatBulkDeleteResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "No comments were deleted.",
                Data = new ChatBulkDeleteResultDTO
                {
                    Deleted = 0,
                    Skipped = skippedIds.Count,
                    SkippedIds = skippedIds
                }
            };
        }

        var deletedChatIds = deletedChats.Select(x => x.Id).ToList();

        var translations = await _uow.TicketChatTranslations.GetAllAsync()
            .Where(t => !t.IsDeleted && deletedChatIds.Contains(t.ChatId))
            .ToListAsync(ct);

        foreach (var t in translations)
        {
            t.IsDeleted = true;
            t.DeletedAt = now;
            _uow.TicketChatTranslations.UpdateAsync(t);
        }

        foreach (var chatId in deletedChatIds)
        {
            await _outboxWriter.WriteAsync(new ChatDeletedEvent(
                chatId,
                ticket.Id,
                request.UserId,
                (int)request.UserRole), ct);
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatBulkDelete] Failed to persist bulk delete for ticket {TicketId}", ticket.Id);
            return Fail(500, "Error saving data. Please try again.");
        }

        // Hide non-owner chats for the caller (idempotent)
        if (chatsToHide.Count > 0)
        {
            var existingHideIds = await _uow.TicketChatHides.GetAllAsync()
                .Where(h => h.UserId == request.UserId && chatsToHide.Contains(h.ChatId) && !h.IsDeleted)
                .Select(h => h.ChatId)
                .ToListAsync(ct);

            foreach (var chatId in chatsToHide.Where(id => !existingHideIds.Contains(id)))
                await _uow.TicketChatHides.AddAsync(new TicketChatHide { ChatId = chatId, UserId = request.UserId });

            if (chatsToHide.Count > existingHideIds.Count)
            {
                try
                { await _uow.SaveChangesAsync(ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "[ChatBulkDelete] Hide save failed for user {UserId}, non-critical", request.UserId); }
            }
        }

        foreach (var t in translations)
            await _cache.RemoveAsync($"chat-translation:{t.ChatId}:{t.TargetLanguage}", ct);

        await _chatCache.InvalidateAsync(ticket.Id, ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatDeleted,
            $"Bulk deleted {deletedChats.Count} chat(s)",
            null,
            null);

        foreach (var (chatId, isInternal) in deletedChats)
        {
            try
            {
                await _realtimeNotifier.NotifyChatDeletedAsync(ticket.Id, chatId, request.UserDisplayName, isInternal, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatBulkDelete] Failed to broadcast ChatDeleted SignalR event for chat {ChatId}", chatId);
            }
        }

        return new ChatBulkDeleteResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Deleted {deletedChats.Count} comment(s).",
            Data = new ChatBulkDeleteResultDTO
            {
                Deleted = deletedChats.Count,
                Skipped = skippedIds.Count,
                SkippedIds = skippedIds
            }
        };
    }

    private static ChatBulkDeleteResponse Fail(int statusCode, string message) =>
        new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
