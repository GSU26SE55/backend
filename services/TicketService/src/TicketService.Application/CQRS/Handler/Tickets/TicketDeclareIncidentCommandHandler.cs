using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketDeclareIncidentCommandHandler : IRequestHandler<TicketDeclareIncidentCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly IActivityLogger _activityLogger;
    private readonly ITicketActivationService _slaTransitions;

    public TicketDeclareIncidentCommandHandler(ITicketUnitOfWork uow,
        IIntegrationEventOutboxWriter outboxWriter, IActivityLogger activityLogger,
        ITicketActivationService slaTransitions) =>
        (_uow, _outboxWriter, _activityLogger, _slaTransitions) =
        (uow, outboxWriter, activityLogger, slaTransitions);

    public async Task<TicketActionResponse> Handle(TicketDeclareIncidentCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.ActiveIncidentEpisodeId.HasValue)
            return Success(ticket, "Ticket is already in active Urgent incident handling.");
        if (ticket.Status is TicketStatusEnum.Completed or TicketStatusEnum.Closed or TicketStatusEnum.ClosedRejected)
            return Fail(409, "A terminal ticket cannot be declared as an incident.");
        if (ticket.Priority != TicketPriorityEnum.P1Critical)
            return Fail(409, "Only a P1Critical ticket can be promoted to Urgent incident handling.");

        try
        {
            await _uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var primary = await _uow.TicketAssignments.GetAllAsync()
                    .FirstOrDefaultAsync(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler, transactionCt);
                var retain = false;
                if (request.KeepCurrentPrimary && primary is not null)
                {
                    var staff = await _uow.StaffAccounts.GetAllAsync().AsNoTracking()
                        .FirstOrDefaultAsync(x => x.AccountId == primary.StaffId && !x.IsDeleted, transactionCt);
                    retain = staff is not null && staff.Status == AccountStatusEnum.Active && staff.IsAvailable &&
                        AssignmentRoleHelper.ValidatePrimaryHandlerTier(TicketPriorityEnum.Urgent, staff.SkillTier);
                }

                if (!retain && primary is not null)
                {
                    primary.Role = AssignmentRoleEnum.PreviousPrimaryHandler;
                    _uow.TicketAssignments.UpdateAsync(primary);
                }
                if (ticket.Status != TicketStatusEnum.Open && !retain)
                    ticket.Status = TicketStatusEnum.ReAssign;
                else if (ticket.Status == TicketStatusEnum.Request)
                    ticket.Status = TicketStatusEnum.ReAssign;

                ticket.Priority = TicketPriorityEnum.Urgent;
                ticket.IsIncident = true;
                ticket.ActiveIncidentEpisodeId = Guid.NewGuid();
                ticket.EscalationReason = EscalationReasonEnum.SafetyConcern;

                await _slaTransitions.StopSlaAsync(ticket, transactionCt);

                var assets = await _uow.TicketBatteryAssets.GetAllAsync().AsNoTracking()
                    .Where(x => x.TicketId == ticket.Id && !x.IsDeleted)
                    .Select(x => x.BatteryAssetId).ToListAsync(transactionCt);
                if (ticket.BatteryAssetId != Guid.Empty && !assets.Contains(ticket.BatteryAssetId))
                    assets.Add(ticket.BatteryAssetId);

                await _activityLogger.LogAsync(ticket.Id, request.UserId, ActorRoleEnum.Manager,
                    request.UserDisplayName, ActivityActionEnum.IncidentDeclared, reason: request.IncidentDescription!.Trim());
                await _outboxWriter.WriteAsync(new IncidentDeclaredEvent(ticket.Id, ticket.Code, request.UserId), transactionCt);
                await _outboxWriter.WriteAsync(new BatteryIsolationRequestedEvent(
                    ticket.ActiveIncidentEpisodeId.Value, ticket.Id, assets, DateTime.UtcNow)
                { Id = DeterministicEventId.From(ticket.ActiveIncidentEpisodeId.Value, "battery-isolation-requested") }, transactionCt);
                await _uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await _uow.Tickets.ReloadAsync(ticket, ct);
            if (ticket.ActiveIncidentEpisodeId.HasValue)
                return Success(ticket, "Ticket is already in active Urgent incident handling.");
            throw;
        }

        return Success(ticket, "Ticket promoted to Urgent incident handling.");
    }

    private static TicketActionResponse Success(TicketService.Domain.Entities.Ticket ticket, string message) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = message,
        Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
    };

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
