using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Tickets;

/// <summary>
/// Manager gộp ticket B vào A: B.MergedIntoTicketId=A, B.Status=Closed (reason "Merged into {A.Code}"),
/// B.ClosedAt=now. Log activity 2 bên. B đã gộp → ẩn khỏi ManagerQueue.
/// </summary>
public class TicketMergeCommandHandler : IRequestHandler<TicketMergeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IActivityLogger _activityLogger;

    public TicketMergeCommandHandler(ITicketUnitOfWork uow, IActivityLogger activityLogger)
    {
        _uow = uow;
        _activityLogger = activityLogger;
    }

    public async Task<TicketActionResponse> Handle(TicketMergeCommand request, CancellationToken ct)
    {
        var source = await _uow.Tickets.GetByIdAsync(request.TicketId);   // Ticket B — bị gộp
        if (source is null || source.IsDeleted)
            return Fail(404, "Không tìm thấy ticket cần gộp.");

        var target = await _uow.Tickets.GetByIdAsync(request.TargetTicketId); // Ticket A — đích
        if (target is null || target.IsDeleted)
            return Fail(404, "Không tìm thấy ticket đích.");

        if (source.MergedIntoTicketId != null)
            return Fail(409, "Ticket đã được gộp trước đó.");

        source.MergedIntoTicketId = target.Id;
        source.Status = TicketStatusEnum.Closed;
        source.Reason = $"Merged into {target.Code}";
        source.ClosedAt = DateTime.UtcNow;

        _uow.Tickets.UpdateAsync(source);

        // Log 2 bên — B đóng vì gộp, A nhận ticket gộp vào.
        await _activityLogger.LogAsync(
            source.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            "Manager",
            ActivityActionEnum.StatusChanged,
            reason: $"Gộp vào ticket {target.Code}");

        await _activityLogger.LogAsync(
            target.Id,
            request.ManagerId,
            ActorRoleEnum.Manager,
            "Manager",
            ActivityActionEnum.StatusChanged,
            reason: $"Ticket {source.Code} được gộp vào ticket này");

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Ticket merged successfully.",
            Data = new TicketActionDTO
            {
                Id = source.Id.ToString(),
                TicketId = source.Id.ToString(),
                Code = source.Code,
                Status = source.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
