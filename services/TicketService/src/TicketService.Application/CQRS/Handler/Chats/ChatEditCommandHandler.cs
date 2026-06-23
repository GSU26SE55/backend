using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatEdit;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatEditCommandHandler : IRequestHandler<ChatEditCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly ChatOptions _chatOptions;

    public ChatEditCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IOptions<ChatOptions> chatOptions)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatOptions = chatOptions.Value;
    }

    public async Task<TicketActionResponse> Handle(ChatEditCommand request, CancellationToken ct)
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
            return Fail(400, "Không thể sửa bình luận khi ticket đã đóng.");

        var isAuthor = chat.AuthorUserId == request.UserId;
        var isManagerOrAdmin = request.UserRole == ActorRoleEnum.Manager || request.UserRole == ActorRoleEnum.Admin;

        if (isAuthor)
        {
            var elapsed = DateTime.UtcNow - chat.CreatedAt;
            if (elapsed > TimeSpan.FromMinutes(_chatOptions.EditWindowMinutes))
                return Fail(403, $"Đã quá thời gian cho phép chỉnh sửa ({_chatOptions.EditWindowMinutes} phút).");
        }
        else if (isManagerOrAdmin)
        {
            if (string.IsNullOrWhiteSpace(request.EditReason))
            {
                var response = new TicketActionResponse
                {
                    IsSuccess = false,
                    StatusCode = 400,
                    Message = "Dữ liệu đầu vào không hợp lệ."
                };
                response.ListErrors.Add(new Errors { Field = "EditReason", Detail = "Bắt buộc nhập lý do khi sửa bình luận của người khác." });
                return response;
            }
        }
        else
        {
            return Fail(403, "Không có quyền sửa bình luận này.");
        }

        var oldBody = chat.Body;

        var chatEdit = new TicketChatEdit
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Chat = chat,
            OldBody = oldBody,
            NewBody = request.Body,
            EditedAt = DateTime.UtcNow,
            EditedByUserId = request.UserId,
            EditedByRole = request.UserRole,
            EditReason = request.EditReason
        };
        await _uow.TicketChatEdits.AddAsync(chatEdit);

        chat.Body = request.Body;
        chat.EditedAt = DateTime.UtcNow;
        chat.EditCount += 1;
        chat.LastEditedByUserId = request.UserId;
        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatEdited,
            oldBody[..Math.Min(oldBody.Length, 50)],
            request.Body[..Math.Min(request.Body.Length, 50)],
            request.EditReason);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Sửa bình luận thành công.",
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
