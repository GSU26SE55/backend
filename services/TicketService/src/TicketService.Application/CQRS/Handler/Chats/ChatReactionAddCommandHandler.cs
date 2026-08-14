using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReactionAddCommandHandler : IRequestHandler<ChatReactionAddCommand, ChatReactionActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<ChatReactionAddCommandHandler> _logger;

    private readonly IPublisher _publisher;   // Sprint Chat DoD — audit chat.reaction

    public ChatReactionAddCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        ILogger<ChatReactionAddCommandHandler> logger,
        IPublisher publisher)
    {
        _publisher = publisher;
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
            // Sprint Chat DoD — thêm `t.Code` vào projection để audit có targetDisplay giống các
            // handler khác. Cùng 1 query đang chạy, không phát sinh round-trip.
            .Select(t => new
            {
                t.Code,
                t.CustomerId,
                PrimaryHandlerStaffId = t.Assignments
                    .Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler)
                    .Select(a => (Guid?)a.StaffId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.UserId, request.ActorRoles))
            return Fail(403, "You do not have permission to access this ticket.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted || chat.TicketId != request.TicketId)
            return Fail(404, "Comment not found.");

        if (chat.IsInternal && !TicketQueryHelper.CanViewInternalChats(request.ActorRoles))
            return Fail(404, "Comment not found.");

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

            // Sprint Chat DoD — audit chat.reaction. Có 2 nhánh (tạo mới / khôi phục bản
            // đã soft-delete) nên publish ở cả hai, ngay trước SaveChanges của nhánh đó.
            await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
                TicketService.Domain.Enums.TicketAuditActionEnum.ChatReacted, request.TicketId, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id, ["reactionType"] = request.ReactionType.ToString() }), ct);

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

            // Sprint Chat DoD — audit chat.reaction. Có 2 nhánh (tạo mới / khôi phục bản
            // đã soft-delete) nên publish ở cả hai, ngay trước SaveChanges của nhánh đó.
            await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
                TicketService.Domain.Enums.TicketAuditActionEnum.ChatReacted, request.TicketId, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id, ["reactionType"] = request.ReactionType.ToString() }), ct);

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
            Message = "Reaction added successfully.",
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
