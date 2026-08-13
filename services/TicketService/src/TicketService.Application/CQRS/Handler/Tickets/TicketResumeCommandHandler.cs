using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

public class TicketResumeCommandHandler : IRequestHandler<TicketResumeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly ITicketActivationService _activationService;

    public TicketResumeCommandHandler(ITicketUnitOfWork uow, ITicketActivationService activationService)
    {
        _uow = uow;
        _activationService = activationService;
    }

    public async Task<TicketActionResponse> Handle(TicketResumeCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.TicketId && !x.IsDeleted, ct);
        if (ticket is null)
            return Fail(404, "Ticket not found.");
        if (ticket.Status != TicketStatusEnum.Pending ||
            (ticket.PendingContext != PendingContextEnum.Held && ticket.PendingContext != PendingContextEnum.Scheduled))
            return Fail(409, "Only a held or scheduled Pending ticket can be resumed early.");

        var primaryStaffId = await _uow.TicketAssignments.GetAllAsync()
            .Where(x => x.TicketId == ticket.Id && !x.IsDeleted && x.Role == AssignmentRoleEnum.PrimaryHandler)
            .Select(x => (Guid?)x.StaffId)
            .SingleOrDefaultAsync(ct);
        if (primaryStaffId != request.StaffId)
            return Fail(403, "Only the active PrimaryHandler can resume this ticket early.");

        ActivationResult? result = null;
        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            ticket.ScheduleVersion++;
            result = await _activationService.ActivateAsync(new ActivationRequest(
                ticket, request.StaffId, ticket.ScheduleVersion, DateTime.UtcNow,
                ActivationReason.EarlyResume, request.StaffId, ActorRoleEnum.Staff,
                request.StaffName ?? "Staff", request.Reason!.Trim()), transactionCt);
            if (result.Activated)
                await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        if (result?.Activated != true)
            return Fail(409, result?.Conflict ?? "The ticket could not be resumed.");

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Work resumed early.",
            Data = new TicketActionDTO { Id = ticket.Id.ToString(), Code = ticket.Code, Status = ticket.Status }
        };
    }

    private static TicketActionResponse Fail(int code, string message) => new()
    { IsSuccess = false, StatusCode = code, Message = message };
}
