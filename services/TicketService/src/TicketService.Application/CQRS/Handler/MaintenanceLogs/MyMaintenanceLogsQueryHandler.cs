using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Query.MaintenanceLogs;
using TicketService.Application.DTOs.Response.Maintenances;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.MaintenanceLogs;

public class MyMaintenanceLogsQueryHandler : IRequestHandler<MyMaintenanceLogsQuery, List<StaffMaintenanceLogGroupDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public MyMaintenanceLogsQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<List<StaffMaintenanceLogGroupDTO>> Handle(MyMaintenanceLogsQuery request, CancellationToken ct)
    {
        var logs = await _uow.MaintenanceLogs.GetAllAsync()
            .Include(m => m.Ticket)
            .Where(m => m.StaffId == request.StaffId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        var groupedLogs = logs.GroupBy(m => new { m.TicketId, m.Ticket.Code, m.Ticket.Title })
            .Select(g => new StaffMaintenanceLogGroupDTO
            {
                TicketId = g.Key.TicketId.ToString(),
                TicketCode = g.Key.Code,
                TicketTitle = g.Key.Title,
                Logs = g.Select(m => new MaintenanceLogDTO
                {
                    Id = m.Id.ToString(),
                    StaffId = m.StaffId.ToString(),
                    LogType = m.LogType,
                    Summary = m.Summary,
                    DiagnosisDetails = m.DiagnosisDetails,
                    ActionsTaken = m.ActionsTaken,
                    DurationMinutes = m.DurationMinutes,
                    ResolutionNote = m.ResolutionNote,
                    StartedAt = m.StartedAt,
                    CompletedAt = m.CompletedAt,
                    AttachmentFileIds = m.AttachmentFileIds.Select(id => id.ToString()).ToList(),
                    BeforePhotosFileIds = m.BeforePhotosFileIds.Select(id => id.ToString()).ToList(),
                    AfterPhotosFileIds = m.AfterPhotosFileIds.Select(id => id.ToString()).ToList(),
                    RelatedKbArticleIds = m.RelatedKbArticleIds.Select(id => id.ToString()).ToList(),
                    CreatedAt = m.CreatedAt
                }).ToList()
            })
            .ToList();

        return groupedLogs;
    }
}
