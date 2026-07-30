using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Participants;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantUpdateRoleCommandHandler : IRequestHandler<ParticipantUpdateRoleCommand, ParticipantActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;

    public ParticipantUpdateRoleCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _activityLogger = activityLogger;
    }

    public async Task<ParticipantActionResponse> Handle(ParticipantUpdateRoleCommand request, CancellationToken ct)
    {
        var isManagerOrAdmin = request.ActorRole == ActorRoleEnum.Manager || request.ActorRole == ActorRoleEnum.Admin;
        if (!isManagerOrAdmin)
            return Fail(403, "Chỉ Manager/Admin mới có quyền cập nhật vai trò participant.");

        var participant = await _uow.TicketParticipants.GetAllAsync()
            .FirstOrDefaultAsync(p => p.TicketId == request.TicketId && p.UserId == request.UserId
                && p.RemovedAt == null && !p.IsDeleted, ct);

        if (participant == null)
            return Fail(404, "Không tìm thấy participant active của ticket.");

        if (participant.ParticipantType == ParticipantTypeEnum.Owner
            || participant.ParticipantType == ParticipantTypeEnum.PrimaryAssignee
            || participant.ParticipantType == ParticipantTypeEnum.PreviousAssignee)
            return Fail(403, "Không thể đổi vai trò Owner/PrimaryAssignee/PreviousAssignee — vai trò này do hệ thống tự quản lý.");

        var oldType = participant.ParticipantType;
        participant.ParticipantType = request.ParticipantType;
        if (request.CanPost.HasValue)
            participant.CanPost = request.CanPost.Value;
        if (request.CanViewInternal.HasValue)
            participant.CanViewInternal = request.CanViewInternal.Value;

        _uow.TicketParticipants.UpdateAsync(participant);
        await _outboxWriter.WriteAsync(new ParticipantRoleChangedEvent(
            request.TicketId,
            participant.UserId,
            (int)oldType,
            (int)participant.ParticipantType,
            request.ActorUserId), ct);

        await _activityLogger.LogAsync(
            request.TicketId,
            request.ActorUserId,
            request.ActorRole,
            request.ActorName,
            ActivityActionEnum.ParticipantRoleChanged,
            oldValue: oldType.ToString(),
            newValue: $"User {participant.UserId} changed to {participant.ParticipantType}.");
        await _uow.SaveChangesAsync(ct);

        return new ParticipantActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật vai trò participant thành công.",
            Data = ToDto(participant)
        };
    }

    private static TicketParticipantDTO ToDto(TicketParticipant p) => new()
    {
        Id = p.Id.ToString(),
        TicketId = p.TicketId.ToString(),
        UserId = p.UserId.ToString(),
        UserRole = p.UserRole,
        ParticipantType = p.ParticipantType,
        CanPost = p.CanPost,
        CanViewInternal = p.CanViewInternal,
        AddedByUserId = p.AddedByUserId.ToString(),
        AddedAt = p.AddedAt
    };

    private static ParticipantActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
