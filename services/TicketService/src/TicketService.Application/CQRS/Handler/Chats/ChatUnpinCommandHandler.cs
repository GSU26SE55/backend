using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatUnpinCommandHandler : IRequestHandler<ChatUnpinCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;

    public ChatUnpinCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger, IChatAuthorizationService chatAuthorizationService)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
    }

    public async Task<TicketActionResponse> Handle(ChatUnpinCommand request, CancellationToken ct)
    {
        if (!_chatAuthorizationService.CanPinChat(request.UserPermissions))
            return Fail(403, "Không có quyền unpin bình luận.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Không tìm thấy bình luận.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (!chat.IsPinned)
            return Fail(400, "Bình luận chưa được pin.");

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

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Unpin bình luận thành công.",
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
