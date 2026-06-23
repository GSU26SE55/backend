using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ParticipantSelfLeave;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantSelfLeaveCommandHandler : IRequestHandler<ParticipantSelfLeaveCommand, ParticipantActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IMessageProducerService _producer;

    public ParticipantSelfLeaveCommandHandler(ITicketUnitOfWork uow, IMessageProducerService producer)
    {
        _uow = uow;
        _producer = producer;
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

        await _uow.SaveChangesAsync(ct);

        await _producer.PublishAsync(new ParticipantRemovedEvent(
            request.TicketId,
            participant.UserId,
            request.ActorUserId,
            request.LeaveReason ?? string.Empty), ct);

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
