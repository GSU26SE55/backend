using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatPinCommandHandler : IRequestHandler<ChatPinCommand, TicketActionResponse>
{
    private const int MaxPinnedPerTicket = 5;

    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly IPublisher _publisher;   // Sprint Chat DoD — audit chat.pin
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<ChatPinCommandHandler> _logger;

    public ChatPinCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IChatAuthorizationService chatAuthorizationService,
        IPublisher publisher,
        ITicketChatRealtimeNotifier realtimeNotifier,
        ILogger<ChatPinCommandHandler> logger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
        _publisher = publisher;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatPinCommand request, CancellationToken ct)
    {
        if (!_chatAuthorizationService.CanPinChat(request.UserPermissions))
            return Fail(403, "You do not have permission to pin comments.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Comment not found.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Comment not found.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (chat.IsPinned)
            return Fail(400, "Comment is already pinned.");

        await _uow.BeginTransactionAsync();
        try
        {
            // Đếm lại ngay trước khi pin, trong cùng transaction — thu hẹp race window khi có request pin đồng thời.
            var pinnedCount = await _uow.TicketChats.GetAllAsync()
                .AsNoTracking()
                .CountAsync(c => c.TicketId == request.TicketId && c.IsPinned && !c.IsDeleted, ct);

            if (pinnedCount >= MaxPinnedPerTicket)
            {
                await _uow.RollbackTransactionAsync();
                return Fail(400, $"Reached the maximum limit of {MaxPinnedPerTicket} pinned comments per ticket.");
            }

            chat.IsPinned = true;
            chat.PinnedAt = DateTime.UtcNow;
            chat.PinnedByUserId = request.UserId;
            _uow.TicketChats.UpdateAsync(chat);

            await _activityLogger.LogAsync(
                ticket.Id,
                request.UserId,
                request.UserRole,
                request.UserDisplayName,
                ActivityActionEnum.ChatPinned,
                null,
                ChatTextHelper.Truncate(chat.Body));

            // Sprint Chat DoD — audit ChatPinned. Publish TRƯỚC SaveChanges để entry audit +
            // outbox đi cùng transaction với thay đổi nghiệp vụ (#AUDIT-25/26).
            await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
                TicketService.Domain.Enums.TicketAuditActionEnum.ChatPinned, ticket.Id, targetDisplay: ticket.Code,
                metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id }), ct);

            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        // After the commit, and never inside it: a SignalR failure must not roll back a pin that
        // is already persisted. Same shape as ChatDelete.
        try
        {
            await _realtimeNotifier.NotifyChatPinChangedAsync(
                ticket.Id, chat.Id, isPinned: true, chat.IsInternal, request.UserDisplayName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatPin] Failed to broadcast ChatPinChanged SignalR event for ticket {TicketId}", ticket.Id);
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Comment pinned successfully.",
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
