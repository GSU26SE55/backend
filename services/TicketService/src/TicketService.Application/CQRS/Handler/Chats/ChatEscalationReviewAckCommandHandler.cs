using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// Manager ACK escalation review — publishes ChatEscalationReviewAckedEvent → saga transitions Pending → Reviewed.
/// #566.
/// </summary>
public class ChatEscalationReviewAckCommandHandler : IRequestHandler<ChatEscalationReviewAckCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public ChatEscalationReviewAckCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<CommonResponse<object>> Handle(ChatEscalationReviewAckCommand request, CancellationToken ct)
    {
        if (request.CurrentUserRole is not (ActorRoleEnum.Manager or ActorRoleEnum.Admin))
            return Fail(403, "Chỉ Manager hoặc Admin mới có thể ACK escalation review.");

        var chat = await _uow.TicketChats.GetAllAsync()
            .Where(c => c.Id == request.ChatId && c.TicketId == request.TicketId && !c.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (chat == null)
            return Fail(404, "Không tìm thấy chat.");

        await _outboxWriter.WriteAsync(new ChatEscalationReviewAckedEvent(
            request.ChatId,
            request.CurrentUserId,
            DateTime.UtcNow), ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Đã ACK escalation review." };
    }

    private static CommonResponse<object> Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
