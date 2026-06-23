using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.ChatDelete;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatDeleteCommandHandler : IRequestHandler<ChatDeleteCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public ChatDeleteCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
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

        if (ticket.Status == TicketStatusEnum.Closed)
            return Fail(400, "Không thể xóa bình luận khi ticket đã đóng.");

        var isAuthor = chat.AuthorUserId == request.UserId;
        var isManagerOrAdmin = request.UserRole == ActorRoleEnum.Manager || request.UserRole == ActorRoleEnum.Admin;

        if (isAuthor)
        {
            // Author xóa chat của mình bất cứ lúc nào — không cần DeleteReason.
        }
        else if (isManagerOrAdmin)
        {
            if (string.IsNullOrWhiteSpace(request.DeleteReason))
            {
                var response = new TicketActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 400,
                    Message = "Dữ liệu đầu vào không hợp lệ."
                };
                response.ListErrors.Add(new Errors { Field = "DeleteReason", Detail = "Bắt buộc nhập lý do khi xóa bình luận của người khác." });
                return response;
            }
        }
        else
        {
            return Fail(403, "Không có quyền xóa bình luận này.");
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
            request.DeleteReason);

        await _uow.SaveChangesAsync(ct);

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
