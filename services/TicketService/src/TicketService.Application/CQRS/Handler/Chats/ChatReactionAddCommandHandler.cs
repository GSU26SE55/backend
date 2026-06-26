using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.ChatReactionAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReactionAddCommandHandler : IRequestHandler<ChatReactionAddCommand, ChatReactionActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<ChatReactionAddCommandHandler> _logger;

    public ChatReactionAddCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        ILogger<ChatReactionAddCommandHandler> logger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<ChatReactionActionResponse> Handle(ChatReactionAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.UserId, request.ActorRoles))
            return Fail(403, "Không có quyền truy cập ticket.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        if (chat.IsInternal && !TicketQueryHelper.CanViewInternalChats(request.ActorRoles))
            return Fail(404, "Không tìm thấy bình luận.");

        // Không filter !IsDeleted — unique index (ChatId, UserId, ReactionType) không phải partial index,
        // nên row soft-deleted vẫn chiếm key. Nếu tồn tại (kể cả đã xóa) → restore thay vì insert mới.
        var existing = await _uow.TicketChatReactions.GetAllAsync()
            .FirstOrDefaultAsync(r => r.ChatId == request.ChatId
                && r.UserId == request.UserId
                && r.ReactionType == request.ReactionType, ct);

        if (existing == null)
        {
            var reaction = new TicketChatReaction
            {
                Id = Guid.NewGuid(),
                ChatId = chat.Id,
                Chat = chat,
                UserId = request.UserId,
                UserRole = request.UserRole,
                ReactionType = request.ReactionType
            };
            await _uow.TicketChatReactions.AddAsync(reaction);

            await _outboxWriter.WriteAsync(new ChatReactedEvent(
                chat.Id,
                chat.TicketId,
                request.UserId,
                (int)request.UserRole,
                (int)request.ReactionType,
                false,
                chat.AuthorUserId), ct);

            await _uow.SaveChangesAsync(ct);
        }
        else if (existing.IsDeleted)
        {
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            _uow.TicketChatReactions.UpdateAsync(existing);

            await _outboxWriter.WriteAsync(new ChatReactedEvent(
                chat.Id,
                chat.TicketId,
                request.UserId,
                (int)request.UserRole,
                (int)request.ReactionType,
                false,
                chat.AuthorUserId), ct);

            await _uow.SaveChangesAsync(ct);
        }

        var allReactions = await _uow.TicketChatReactions.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.ChatId == request.ChatId && !r.IsDeleted)
            .ToListAsync(ct);

        var aggregate = ChatReactionAggregateHelper.Build(allReactions);

        try
        {
            await _realtimeNotifier.NotifyReactionChangedAsync(chat.TicketId, chat.Id, chat.IsInternal, aggregate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatReactionAdd] Failed to broadcast ReactionChanged SignalR event for chat {ChatId}", chat.Id);
        }

        return new ChatReactionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Thêm reaction thành công.",
            Data = aggregate
        };
    }

    private static ChatReactionActionResponse Fail(int statusCode, string message)
    {
        return new ChatReactionActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
