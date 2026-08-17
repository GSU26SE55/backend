using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

/// <summary>Closes a New source ticket as a duplicate of a manager-selected master ticket.</summary>
public class TicketMergeCommandHandler : IRequestHandler<TicketMergeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public TicketMergeCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<TicketActionResponse> Handle(TicketMergeCommand request, CancellationToken ct)
    {
        var source = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TicketId && !t.IsDeleted, ct);
        var master = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.TargetTicketId && !t.IsDeleted, ct);

        if (source is null || master is null)
            return Fail(404, "Source ticket or master ticket not found.");
        if (source.Id == master.Id)
            return Fail(400, "Cannot merge a ticket into itself.");
        if (source.Status == TicketStatusEnum.Closed || master.Status == TicketStatusEnum.Closed ||
            source.MergedIntoTicketId.HasValue || master.MergedIntoTicketId.HasValue)
            return Fail(409, "Ticket is closed or already merged and cannot be changed.");
        if (source.Status != TicketStatusEnum.Open)
            return Fail(409, "Only an Open source ticket can be merged.");
        if (master.Status is TicketStatusEnum.Completed or TicketStatusEnum.Closed or TicketStatusEnum.ClosedRejected)
            return Fail(409, "The master ticket must be active.");
        if (source.CustomerId != master.CustomerId)
            return Fail(409, "Both tickets must belong to the same customer.");

        var sourceBatteryIds = await _uow.TicketBatteryAssets.GetAllAsync()
            .Where(x => x.TicketId == source.Id && !x.IsDeleted)
            .Select(x => x.BatteryAssetId)
            .ToListAsync(ct);
        if (source.BatteryAssetId != Guid.Empty && !sourceBatteryIds.Contains(source.BatteryAssetId))
            sourceBatteryIds.Add(source.BatteryAssetId);

        var masterBatteryIds = await _uow.TicketBatteryAssets.GetAllAsync()
            .Where(x => x.TicketId == master.Id && !x.IsDeleted)
            .Select(x => x.BatteryAssetId)
            .ToListAsync(ct);
        if (master.BatteryAssetId != Guid.Empty && !masterBatteryIds.Contains(master.BatteryAssetId))
            masterBatteryIds.Add(master.BatteryAssetId);

        var hasCommonBattery = sourceBatteryIds.Intersect(masterBatteryIds).Any();
        if (!hasCommonBattery)
            return Fail(409, "Both tickets must share at least one battery asset.");

        var managerDisplayName = request.ManagerName!;

        try
        {
            await _uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var attachments = await _uow.TicketAttachments.GetAllAsync()
                    .Where(a => a.TicketId == source.Id && !a.IsDeleted)
                    .ToListAsync(transactionCt);
                foreach (var attachment in attachments)
                {
                    attachment.TicketId = master.Id;
                    attachment.SourceTicketId = source.Id;
                    _uow.TicketAttachments.UpdateAsync(attachment);
                }

                var sourceTimer = await _uow.SlaTimers.GetAllAsync()
                    .FirstOrDefaultAsync(t => t.TicketId == source.Id && !t.IsDeleted, transactionCt);
                if (sourceTimer is not null)
                {
                    sourceTimer.Status = SlaTimerStatusEnum.Stopped;
                    sourceTimer.CurrentPauseStartedAt = null;
                    _uow.SlaTimers.UpdateAsync(sourceTimer);
                }

                var now = DateTime.UtcNow;
                source.Status = TicketStatusEnum.Closed;
                source.CloseReason = TicketCloseReasonEnum.MergedDuplicate;
                source.ClosedAt = now;
                source.MergedIntoTicketId = master.Id;
                _uow.Tickets.UpdateAsync(source);

                await _uow.TicketActivities.AddAsync(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = source.Id,
                    ActorUserId = request.ManagerId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = managerDisplayName,
                    Action = ActivityActionEnum.StatusChanged,
                    Ticket = source,
                    NewValue = TicketStatusEnum.Closed.ToString(),
                    Reason = $"Merged into master ticket {master.Code} ({master.Id})."
                });
                await _uow.TicketActivities.AddAsync(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = master.Id,
                    SourceTicketId = source.Id,
                    ActorUserId = request.ManagerId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = managerDisplayName,
                    Action = ActivityActionEnum.StatusChanged,
                    Ticket = master,
                    Reason = $"Source ticket {source.Code} ({source.Id}) was merged into this ticket."
                });

                await _outboxWriter.WriteAsync(new TicketMergedEvent(
                    source.Id, source.Code, source.CustomerId, master.Id, master.Code, request.ManagerId), transactionCt);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(409, "Ticket was changed by another operation. Please reload and try again.");
        }
        catch
        {
            throw;
        }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Tickets merged successfully.",
            Data = new TicketActionDTO { Id = source.Id.ToString(), TicketId = source.Id.ToString(), Code = source.Code, Status = source.Status }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
