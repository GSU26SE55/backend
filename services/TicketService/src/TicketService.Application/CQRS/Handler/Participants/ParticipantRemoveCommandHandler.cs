using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantRemoveCommandHandler : IRequestHandler<ParticipantRemoveCommand, ParticipantActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketChatRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<ParticipantRemoveCommandHandler> _logger;

    public ParticipantRemoveCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter producer,
        IActivityLogger activityLogger,
        ITicketChatRealtimeNotifier realtimeNotifier,
        ILogger<ParticipantRemoveCommandHandler> logger)
    {
        _uow = uow;
        _outboxWriter = producer;
        _activityLogger = activityLogger;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<ParticipantActionResponse> Handle(ParticipantRemoveCommand request, CancellationToken ct)
    {
        var ticketExists = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (!ticketExists)
            return Fail(404, "Không tìm thấy Ticket.");

        var participant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == request.TicketId && p.UserId == request.UserId
                && p.RemovedAt == null && !p.IsDeleted, ct);

        if (participant == null)
            return Fail(404, "Không tìm thấy participant active của ticket.");

        var isManagerOrAdmin = request.ActorRole == ActorRoleEnum.Manager || request.ActorRole == ActorRoleEnum.Admin;
        if (!isManagerOrAdmin)
            return Fail(403, "Không có quyền xóa participant của ticket này.");

        if (participant.ParticipantType == ParticipantTypeEnum.Owner)
        {
            if (request.ActorRole != ActorRoleEnum.Admin)
                return Fail(403, "Chỉ Admin mới có quyền xóa Owner của ticket.");

            if (string.IsNullOrWhiteSpace(request.RemoveReason))
                return Fail(400, "Bắt buộc nhập lý do khi xóa Owner của ticket.");

            _logger.LogCritical(
                "[ParticipantRemove] Admin {ActorUserId} removed Owner {OwnerUserId} from ticket {TicketId}. Reason: {Reason}",
                request.ActorUserId, participant.UserId, request.TicketId, request.RemoveReason);
        }

        participant.RemovedAt = DateTime.UtcNow;
        participant.RemovedByUserId = request.ActorUserId;
        participant.RemoveReason = request.RemoveReason;
        _uow.TicketParticipants.UpdateAsync(participant);

        await _outboxWriter.WriteAsync(new ParticipantRemovedEvent(
            request.TicketId,
            participant.UserId,
            request.ActorUserId,
            request.RemoveReason ?? string.Empty), ct);

        await _activityLogger.LogAsync(
            request.TicketId,
            request.ActorUserId,
            request.ActorRole,
            request.ActorName,
            ActivityActionEnum.ParticipantRemoved,
            oldValue: participant.ParticipantType.ToString(),
            newValue: $"User {participant.UserId} removed from ticket.",
            reason: request.RemoveReason);
        await _uow.SaveChangesAsync(ct);

        try
        {
            await _realtimeNotifier.ForceDisconnectFromTicketAsync(request.TicketId, participant.UserId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ParticipantRemove] Failed to force disconnect user {UserId} from ticket {TicketId}", participant.UserId, request.TicketId);
        }

        return new ParticipantActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa participant thành công."
        };
    }

    private static ParticipantActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
