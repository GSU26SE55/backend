using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// Admin override Edit — bypass block Closed/ClosedPendingRate + bỏ qua edit window/own-any check (#517).
/// Mirror logic của <see cref="ChatEditCommandHandler"/>.
/// </summary>
public class ChatOverrideEditCommandHandler : IRequestHandler<ChatOverrideEditCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;
    private readonly IMarkdownRenderer _markdownRenderer;

    public ChatOverrideEditCommandHandler(
        ITicketUnitOfWork uow,
        IActivityLogger activityLogger,
        IMarkdownRenderer markdownRenderer)
    {
        _uow = uow;
        _activityLogger = activityLogger;
        _markdownRenderer = markdownRenderer;
    }

    public async Task<TicketActionResponse> Handle(ChatOverrideEditCommand request, CancellationToken ct)
    {
        if (request.UserRole != ActorRoleEnum.Admin)
            return Fail(403, "Only Admin can override when the ticket is closed.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null || chat.IsDeleted)
            return Fail(404, "Comment not found.");

        if (chat.TicketId != request.TicketId)
            return Fail(404, "Comment not found.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Ticket not found.");

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
            EditReason = request.OverrideReason
        };
        await _uow.TicketChatEdits.AddAsync(chatEdit);

        chat.Body = request.Body;
        chat.EditedAt = DateTime.UtcNow;
        chat.EditCount += 1;
        chat.LastEditedByUserId = request.UserId;

        if (chat.BodyFormat == ChatBodyFormatEnum.Markdown)
            chat.BodyHtml = _markdownRenderer.RenderToHtml(chat.Body, chat.AttachmentFileIds);

        _uow.TicketChats.UpdateAsync(chat);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.UserId,
            request.UserRole,
            request.UserDisplayName,
            ActivityActionEnum.ChatEdited,
            ChatTextHelper.Truncate(oldBody),
            ChatTextHelper.Truncate(request.Body),
            request.OverrideReason);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Comment edited (override) successfully.",
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
