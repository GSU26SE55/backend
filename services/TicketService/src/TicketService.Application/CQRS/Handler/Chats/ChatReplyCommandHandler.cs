using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ChatReply;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatReplyCommandHandler : IRequestHandler<ChatReplyCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public ChatReplyCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _outboxWriter = outboxWriter;
    }

    public async Task<TicketActionResponse> Handle(ChatReplyCommand request, CancellationToken ct)
    {
        var parent = await _uow.TicketChats.GetByIdAsync(request.ParentChatId);
        if (parent == null || parent.IsDeleted)
            return Fail(404, "Không tìm thấy bình luận.");

        if (parent.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy bình luận.");

        if (parent.ParentChatId != null)
            return Fail(400, "Không thể reply lồng cấp 2.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        if (ticket.Status == TicketStatusEnum.Closed)
            return Fail(400, "Không thể trả lời bình luận khi ticket đã đóng.");

        var reply = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            AuthorUserId = request.UserId,
            AuthorRole = request.UserRole,
            AuthorDisplayName = request.UserDisplayName,
            Body = request.Body,
            IsInternal = request.IsInternal,
            ParentChatId = parent.Id,
            ThreadRootId = parent.ThreadRootId ?? parent.Id
        };

        await _uow.TicketChats.AddAsync(reply);

        parent.ReplyCount += 1;
        _uow.TicketChats.UpdateAsync(parent);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatReplied,
            null,
            request.IsInternal ? "[Nội bộ]" : "[Công khai]",
            $"Đã trả lời tin nhắn chat: {request.Body[..Math.Min(request.Body.Length, 50)]}...");

        await _outboxWriter.WriteAsync(new ChatCreatedEvent(
            reply.Id,
            reply.TicketId,
            reply.AuthorUserId,
            (int)reply.AuthorRole,
            reply.AuthorDisplayName,
            reply.Body,
            reply.IsInternal,
            reply.AttachmentFileIds,
            ticket.CustomerId,
            ticket.AssignedStaffId), ct);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Trả lời bình luận thành công.",
            Data = new TicketActionDTO
            {
                Id = reply.Id.ToString(),
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
