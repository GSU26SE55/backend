using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Query.MaintenanceLogs;
using TicketService.Application.DTOs.Response.Maintenances;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.MaintenanceLogs;

public class MaintenanceLogsByTicketQueryHandler : IRequestHandler<MaintenanceLogsByTicketQuery, List<MaintenanceLogDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public MaintenanceLogsByTicketQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<List<MaintenanceLogDTO>> Handle(MaintenanceLogsByTicketQuery request, CancellationToken ct)
    {
        var logs = await _uow.MaintenanceLogs.GetAllAsync()
            .Where(m => m.TicketId == request.TicketId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

        return logs.Select(m => new MaintenanceLogDTO
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
        }).ToList();
    }
}
