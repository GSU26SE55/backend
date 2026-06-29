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

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReactionRemoveCommandHandler : IRequestHandler<ChatReactionRemoveCommand, ChatReactionActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<ChatReactionRemoveCommandHandler> _logger;

    public ChatReactionRemoveCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        ITicketChatRealtimeNotifier realtimeNotifier,
        ILogger<ChatReactionRemoveCommandHandler> logger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<ChatReactionActionResponse> Handle(ChatReactionRemoveCommand request, CancellationToken ct)
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

        var existing = await _uow.TicketChatReactions.GetAllAsync()
            .FirstOrDefaultAsync(r => r.ChatId == request.ChatId
                && r.UserId == request.UserId
                && r.ReactionType == request.ReactionType
                && !r.IsDeleted, ct);

        if (existing != null)
        {
            _uow.TicketChatReactions.DeleteAsync(existing);

            await _outboxWriter.WriteAsync(new ChatReactedEvent(
                chat.Id,
                chat.TicketId,
                request.UserId,
                (int)request.UserRole,
                (int)request.ReactionType,
                true,
                chat.AuthorUserId), ct);

            await _uow.SaveChangesAsync(ct);
        }

        var allReactions = await _uow.TicketChatReactions.GetAllAsync()
            .AsNoTracking()
            .Where(r => r.ChatId == request.ChatId && !r.IsDeleted)
            .ToListAsync(ct);

        var aggregate = ChatReactionAggregateHelper.Build(allReactions);

        if (existing != null)
        {
            try
            {
                await _realtimeNotifier.NotifyReactionChangedAsync(chat.TicketId, chat.Id, chat.IsInternal, aggregate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatReactionRemove] Failed to broadcast ReactionChanged SignalR event for chat {ChatId}", chat.Id);
            }
        }

        return new ChatReactionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa reaction thành công.",
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
