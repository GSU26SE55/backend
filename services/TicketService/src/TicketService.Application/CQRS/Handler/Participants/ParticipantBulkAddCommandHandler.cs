using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ParticipantBulkAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Participants;

public class ParticipantBulkAddCommandHandler : IRequestHandler<ParticipantBulkAddCommand, ParticipantBulkActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IMessageProducerService _producer;

    public ParticipantBulkAddCommandHandler(ITicketUnitOfWork uow, IMessageProducerService producer)
    {
        _uow = uow;
        _producer = producer;
    }

    public async Task<ParticipantBulkActionResponse> Handle(ParticipantBulkAddCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);

        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var isManagerOrAdmin = request.ActorRole == ActorRoleEnum.Manager || request.ActorRole == ActorRoleEnum.Admin;
        if (!isManagerOrAdmin)
            return Fail(403, "Chỉ Manager/Admin mới có quyền thêm participant theo lô.");

        var activeUserIds = await _uow.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => p.UserId)
            .ToListAsync(ct);

        foreach (var item in request.Participants)
        {
            if (activeUserIds.Contains(item.UserId))
                return Fail(400, $"Người dùng {item.UserId} đã là participant active của ticket.");
        }

        var participants = request.Participants.Select(item => new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = item.UserId,
            UserRole = item.UserRole,
            ParticipantType = item.ParticipantType,
            CanPost = item.CanPost,
            CanViewInternal = item.CanViewInternal,
            AddedByUserId = request.ActorUserId,
            AddedAt = DateTime.UtcNow
        }).ToList();

        await _uow.BeginTransactionAsync();
        try
        {
            foreach (var participant in participants)
                await _uow.TicketParticipants.AddAsync(participant);

            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            return Fail(500, $"Thêm participant theo lô thất bại: {ex.Message}");
        }

        foreach (var participant in participants)
        {
            await _producer.PublishAsync(new ParticipantAddedEvent(
                ticket.Id,
                participant.UserId,
                (int)participant.UserRole,
                (int)participant.ParticipantType,
                request.ActorUserId), ct);
        }

        return new ParticipantBulkActionResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Thêm participant theo lô thành công.",
            Data = participants.Select(ToDto).ToList()
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

    private static ParticipantBulkActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
