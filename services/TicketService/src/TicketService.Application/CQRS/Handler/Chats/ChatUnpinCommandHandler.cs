using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatUnpinCommandHandler : IRequestHandler<ChatUnpinCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;

    private readonly IPublisher _publisher;   // Sprint Chat DoD — audit chat.unpin

    public ChatUnpinCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger, IChatAuthorizationService chatAuthorizationService, IPublisher publisher)
    {
        _publisher = publisher;
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
    }

    public async Task<TicketActionResponse> Handle(ChatUnpinCommand request, CancellationToken ct)
    {
        if (!_chatAuthorizationService.CanPinChat(request.UserPermissions))
            return Fail(403, "You do not have permission to unpin comments.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Comment not found.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Comment not found.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

        if (!chat.IsPinned)
            return Fail(400, "Comment is not pinned.");

        chat.IsPinned = false;
        chat.PinnedAt = null;
        chat.PinnedByUserId = null;
        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatUnpinned,
            ChatTextHelper.Truncate(chat.Body),
            null);

        // Sprint Chat DoD — audit ChatUnpinned. Publish TRƯỚC SaveChanges để entry audit +
        // outbox đi cùng transaction với thay đổi nghiệp vụ (#AUDIT-25/26).
        await _publisher.Publish(TicketService.Application.CQRS.Notification.Audit.TicketAuditTrailNotification.For(
            TicketService.Domain.Enums.TicketAuditActionEnum.ChatUnpinned, ticket.Id, targetDisplay: ticket.Code,
            metadata: new Dictionary<string, object?> { ["chatId"] = chat.Id }), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Comment unpinned successfully.",
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
