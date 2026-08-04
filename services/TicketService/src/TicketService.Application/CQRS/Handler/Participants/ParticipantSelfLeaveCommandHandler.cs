using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantSelfLeaveCommandHandler : IRequestHandler<ParticipantSelfLeaveCommand, ParticipantActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;

    public ParticipantSelfLeaveCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _activityLogger = activityLogger;
    }

    public async Task<ParticipantActionResponse> Handle(ParticipantSelfLeaveCommand request, CancellationToken ct)
    {
        var participant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == request.TicketId && p.UserId == request.ActorUserId
                && p.RemovedAt == null && !p.IsDeleted, ct);

        if (participant == null)
            return Fail(404, "Không tìm thấy participant active của ticket.");

        if (participant.ParticipantType != ParticipantTypeEnum.Watcher)
            return Fail(403, "Chỉ Watcher mới có thể tự rời khỏi ticket.");

        participant.RemovedAt = DateTime.UtcNow;
        participant.RemovedByUserId = request.ActorUserId;
        participant.RemoveReason = request.LeaveReason;
        _uow.TicketParticipants.UpdateAsync(participant);

        await _outboxWriter.WriteAsync(new ParticipantRemovedEvent(
            request.TicketId,
            participant.UserId,
            request.ActorUserId,
            request.LeaveReason ?? string.Empty), ct);

        await _activityLogger.LogAsync(
            request.TicketId,
            request.ActorUserId,
            participant.UserRole,
            request.ActorName,
            ActivityActionEnum.ParticipantRemoved,
            oldValue: participant.ParticipantType.ToString(),
            newValue: $"User {participant.UserId} left the ticket.",
            reason: request.LeaveReason);
        await _uow.SaveChangesAsync(ct);

        return new ParticipantActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Rời khỏi ticket thành công."
        };
    }

    private static ParticipantActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
