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

public class ParticipantAddCommandHandler : IRequestHandler<ParticipantAddCommand, ParticipantActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;

    public ParticipantAddCommandHandler(
        ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter,
        IActivityLogger activityLogger)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
        _activityLogger = activityLogger;
    }

    public async Task<ParticipantActionResponse> Handle(ParticipantAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var activeParticipants = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .ToListAsync(ct);

        var isManagerOrAdmin = request.ActorRole == ActorRoleEnum.Manager || request.ActorRole == ActorRoleEnum.Admin;
        var isPrimaryAssignee = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.ParticipantType == ParticipantTypeEnum.PrimaryAssignee);

        if (!isManagerOrAdmin && !isPrimaryAssignee)
            return Fail(403, "Không có quyền thêm participant vào ticket này.");

        if (activeParticipants.Any(p => p.UserId == request.UserId))
            return Fail(400, "Người dùng này đã là participant active của ticket.");

        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = request.UserId,
            UserRole = request.UserRole,
            ParticipantType = request.ParticipantType,
            CanPost = request.CanPost,
            CanViewInternal = request.CanViewInternal,
            AddedByUserId = request.ActorUserId,
            AddedAt = DateTime.UtcNow
        };

        await _uow.TicketParticipants.AddAsync(participant);
        await _outboxWriter.WriteAsync(new ParticipantAddedEvent(
            ticket.Id,
            participant.UserId,
            (int)participant.UserRole,
            (int)participant.ParticipantType,
            request.ActorUserId), ct);

        await _activityLogger.LogAsync(
            ticket.Id,
            request.ActorUserId,
            request.ActorRole,
            request.ActorName,
            ActivityActionEnum.ParticipantAdded,
            newValue: $"User {participant.UserId} added as {participant.ParticipantType}.");
        await _uow.SaveChangesAsync(ct);

        return new ParticipantActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm participant thành công.",
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
