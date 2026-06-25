using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatDelete;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatDeleteCommandHandler : IRequestHandler<ChatDeleteCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ChatOptions _chatOptions;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public ChatDeleteCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IChatAuthorizationService chatAuthorizationService,
        IOptions<ChatOptions> chatOptions,
        IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _chatAuthorizationService = chatAuthorizationService;
        _chatOptions = chatOptions.Value;
        _outboxWriter = outboxWriter;
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

        var blockReason = ChatClosedStateHelper.GetBlockReason(
            ticket.Status, request.UserRole, ChatClosedStateHelper.ChatAction.Delete, _chatOptions.BlockEditOnClosed);
        if (blockReason != null)
            return Fail(400, blockReason);

        var authResult = _chatAuthorizationService.CanDeleteChat(
            chat,
            request.UserId,
            request.UserPermissions,
            !string.IsNullOrWhiteSpace(request.DeleteReason));

        if (authResult == ChatAuthorizationResult.ReasonRequired)
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

        if (authResult == ChatAuthorizationResult.Forbidden)
            return Fail(403, "Không có quyền xóa bình luận này.");

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

        await _outboxWriter.WriteAsync(new ChatDeletedEvent(
            chat.Id,
            chat.TicketId,
            request.UserId,
            (int)request.UserRole), ct);

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
