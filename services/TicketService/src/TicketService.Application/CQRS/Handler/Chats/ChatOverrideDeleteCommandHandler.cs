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

/// <summary>
/// Admin override Delete — bypass block Closed/ClosedPendingRate + bỏ qua own-any check (#517).
/// Mirror logic của <see cref="ChatDeleteCommandHandler"/>.
/// </summary>
public class ChatOverrideDeleteCommandHandler : IRequestHandler<ChatOverrideDeleteCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public ChatOverrideDeleteCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(ChatOverrideDeleteCommand request, CancellationToken ct)
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
            request.OverrideReason);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Comment deleted (override) successfully.",
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
