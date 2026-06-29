using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatRestoreCommandHandler : IRequestHandler<ChatRestoreCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public ChatRestoreCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(ChatRestoreCommand request, CancellationToken ct)
    {
        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null)
            return Fail(404, "Không tìm thấy bình luận.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        if (!chat.IsDeleted)
            return Fail(400, "Bình luận chưa bị xóa.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        chat.IsDeleted = false;
        chat.DeletedAt = null;
        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatRestored,
            null,
            ChatTextHelper.Truncate(chat.Body));

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Khôi phục bình luận thành công.",
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
