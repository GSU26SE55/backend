using BatteryService.Application.CQRS.Command.EnvironmentalIncident;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using IncidentEntity = BatteryService.Domain.Entities.EnvironmentalIncident;

namespace BatteryService.Application.CQRS.Handler.EnvironmentalIncident;

public class ReportEnvironmentalIncidentCommandHandler
    : IRequestHandler<ReportEnvironmentalIncidentCommand, EnvironmentalIncidentResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outbox;
    private readonly IEnvironmentalMetricsRecorder _metrics;

    public ReportEnvironmentalIncidentCommandHandler(
        IBatteryUnitOfWork uow,
        IIntegrationEventOutboxWriter outbox,
        IEnvironmentalMetricsRecorder metrics)
    {
        _uow = uow;
        _outbox = outbox;
        _metrics = metrics;
    }

    public async Task<EnvironmentalIncidentResponse> Handle(ReportEnvironmentalIncidentCommand request, CancellationToken cancellationToken)
    {
        // GH-806 — thiết bị chỉ được tạo sự cố cho ĐÚNG site của nó, và site phải có thật.
        // Trước đây SiteId lấy thẳng từ body: thiết bị bị chiếm quyền tạo được sự cố giả (cháy,
        // khói, ngập) cho khách hàng khác.
        var existingSites = await _uow.Sites.GetAllAsync()
            .Where(site => !site.IsDeleted && site.Id == request.SiteId)
            .Select(site => site.Id)
            .ToListAsync(cancellationToken);

        var access = IotSiteAccessGuard.Check(
            request.AuthenticatedDeviceSiteId, [request.SiteId], existingSites);
        if (!access.Allowed)
        {
            return new EnvironmentalIncidentResponse
            {
                IsSuccess = false,
                StatusCode = access.StatusCode,
                Message = access.Message
            };
        }

        // Dedup — không tạo nếu đã có Open/Acknowledged cùng (site, incidentType).
        var existing = await _uow.EnvironmentalIncidents.GetAllAsync()
            .Where(i => !i.IsDeleted
                        && i.SiteId == request.SiteId
                        && i.IncidentType == request.IncidentType
                        && (i.Status == EnvironmentalIncidentStatusEnum.Open
                            || i.Status == EnvironmentalIncidentStatusEnum.Acknowledged))
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return new EnvironmentalIncidentResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Existing active incident reused.",
                Data = Map(existing)
            };
        }

        var incident = new IncidentEntity
        {
            Id = Guid.NewGuid(),
            SiteId = request.SiteId,
            IncidentType = request.IncidentType,
            Severity = request.Severity,
            Status = EnvironmentalIncidentStatusEnum.Open,
            ReportedBy = request.ReportedBy,
            DetectedAt = request.DetectedAt.Kind == DateTimeKind.Utc
                ? request.DetectedAt
                : request.DetectedAt.ToUniversalTime(),
            Notes = request.Notes
        };

        await _uow.EnvironmentalIncidents.AddAsync(incident);

        // Site-level alert kèm EnvironmentalIncidentId.
        var alert = new AlertEntity
        {
            Id = Guid.NewGuid(),
            SiteId = request.SiteId,
            BatteryAssetId = null,
            EnvironmentalIncidentId = incident.Id,
            AnomalyType = AnomalyTypeEnum.EnvironmentalIncident,
            Severity = request.Severity,
            ThresholdValue = 0,
            ActualValue = 0,
            Unit = "incident",
            DetectedAt = incident.DetectedAt,
            Status = AlertStatusEnum.Open,
            DedupWindowEndUtc = incident.DetectedAt.AddHours(1)
        };
        await _uow.Alerts.AddAsync(alert);

        // Lookup Site cho event payload — consumer cần CustomerId/SiteName mà không phải gọi back.
        var site = await _uow.Sites.GetAllAsync()
            .Where(s => !s.IsDeleted && s.Id == incident.SiteId)
            .Select(s => new { s.CustomerId, s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        var evt = new EnvironmentalIncidentDetectedEvent(
            IncidentId: incident.Id,
            SiteId: incident.SiteId,
            CustomerId: site?.CustomerId ?? Guid.Empty,
            SiteName: site?.Name ?? string.Empty,
            IncidentType: (int)incident.IncidentType,
            Severity: (int)incident.Severity,
            DetectedAt: incident.DetectedAt,
            AlertId: alert.Id,
            Description: incident.Notes);
        await _outbox.WriteAsync(evt, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        // Sprint 7 #118 — metric cho AlertManager (detection latency = lag từ DetectedAt đến lúc ghi nhận).
        var detectionLatency = Math.Max(0, (DateTime.UtcNow - incident.DetectedAt).TotalSeconds);
        _metrics.IncidentDetected(incident.IncidentType.ToString(), incident.Severity.ToString(), detectionLatency);

        return new EnvironmentalIncidentResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = Map(incident)
        };
    }

    internal static EnvironmentalIncidentDto Map(IncidentEntity i) => new()
    {
        Id = i.Id.ToString(),
        SiteId = i.SiteId.ToString(),
        IncidentType = i.IncidentType,
        Status = i.Status,
        Severity = i.Severity,
        Notes = i.Notes,
        ReportedBy = i.ReportedBy,
        DetectedAt = i.DetectedAt,
        AcknowledgedAt = i.AcknowledgedAt,
        ResolvedAt = i.ResolvedAt,
        ResolutionNote = i.ResolutionNote,
        FalseAlarmAt = i.FalseAlarmAt,
        FalseAlarmReason = i.FalseAlarmReason,
        CreatedAt = i.CreatedAt
    };
}

public class AcknowledgeEnvironmentalIncidentCommandHandler
    : IRequestHandler<AcknowledgeEnvironmentalIncidentCommand, EnvironmentalIncidentResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    public AcknowledgeEnvironmentalIncidentCommandHandler(IBatteryUnitOfWork uow) { _uow = uow; }

    public async Task<EnvironmentalIncidentResponse> Handle(AcknowledgeEnvironmentalIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await _uow.EnvironmentalIncidents.GetByIdAsync(request.Id);
        if (incident is null || incident.IsDeleted)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 404, Message = "Incident not found." };

        if (incident.Status != EnvironmentalIncidentStatusEnum.Open)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 409, Message = $"Cannot acknowledge incident in state {incident.Status}." };

        incident.Status = EnvironmentalIncidentStatusEnum.Acknowledged;
        incident.AcknowledgedBy = request.AcknowledgedBy;
        incident.AcknowledgedAt = DateTime.UtcNow;
        _uow.EnvironmentalIncidents.UpdateAsync(incident);
        await _uow.SaveChangesAsync(cancellationToken);

        return new EnvironmentalIncidentResponse { IsSuccess = true, StatusCode = 200, Data = ReportEnvironmentalIncidentCommandHandler.Map(incident) };
    }
}

public class ResolveEnvironmentalIncidentCommandHandler
    : IRequestHandler<ResolveEnvironmentalIncidentCommand, EnvironmentalIncidentResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outbox;

    public ResolveEnvironmentalIncidentCommandHandler(IBatteryUnitOfWork uow, IIntegrationEventOutboxWriter outbox)
    {
        _uow = uow;
        _outbox = outbox;
    }

    public async Task<EnvironmentalIncidentResponse> Handle(ResolveEnvironmentalIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await _uow.EnvironmentalIncidents.GetAllAsync()
            .Include(i => i.Alerts)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);

        if (incident is null || incident.IsDeleted)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 404, Message = "Incident not found." };

        if (incident.Status is EnvironmentalIncidentStatusEnum.Resolved or EnvironmentalIncidentStatusEnum.FalseAlarm)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 409, Message = "Already terminal." };

        var now = DateTime.UtcNow;
        incident.Status = EnvironmentalIncidentStatusEnum.Resolved;
        incident.ResolvedBy = request.ResolvedBy;
        incident.ResolvedAt = now;
        incident.ResolutionNote = request.ResolutionNote;
        _uow.EnvironmentalIncidents.UpdateAsync(incident);

        // Close kèm các alert site-level liên kết.
        foreach (var alert in incident.Alerts.Where(a => !a.IsDeleted && a.Status != AlertStatusEnum.Resolved))
        {
            alert.Status = AlertStatusEnum.Resolved;
            alert.ResolvedAt = now;
            _uow.Alerts.UpdateAsync(alert);
        }

        await _outbox.WriteAsync(new EnvironmentalIncidentResolvedEvent(
            IncidentId: incident.Id,
            SiteId: incident.SiteId,
            ResolvedAt: now,
            ResolvedByUserId: request.ResolvedBy,
            WasFalseAlarm: false,
            ResolutionNote: request.ResolutionNote), cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new EnvironmentalIncidentResponse { IsSuccess = true, StatusCode = 200, Data = ReportEnvironmentalIncidentCommandHandler.Map(incident) };
    }
}

public class MarkFalseAlarmEnvironmentalIncidentCommandHandler
    : IRequestHandler<MarkFalseAlarmEnvironmentalIncidentCommand, EnvironmentalIncidentResponse>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outbox;

    public MarkFalseAlarmEnvironmentalIncidentCommandHandler(IBatteryUnitOfWork uow, IIntegrationEventOutboxWriter outbox)
    {
        _uow = uow;
        _outbox = outbox;
    }

    public async Task<EnvironmentalIncidentResponse> Handle(MarkFalseAlarmEnvironmentalIncidentCommand request, CancellationToken cancellationToken)
    {
        var incident = await _uow.EnvironmentalIncidents.GetAllAsync()
            .Include(i => i.Alerts)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken);

        if (incident is null || incident.IsDeleted)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 404, Message = "Incident not found." };

        if (incident.Status is EnvironmentalIncidentStatusEnum.Resolved or EnvironmentalIncidentStatusEnum.FalseAlarm)
            return new EnvironmentalIncidentResponse { IsSuccess = false, StatusCode = 409, Message = "Already terminal." };

        var now = DateTime.UtcNow;
        incident.Status = EnvironmentalIncidentStatusEnum.FalseAlarm;
        incident.FalseAlarmBy = request.FalseAlarmBy;
        incident.FalseAlarmAt = now;
        incident.FalseAlarmReason = request.FalseAlarmReason;
        _uow.EnvironmentalIncidents.UpdateAsync(incident);

        foreach (var alert in incident.Alerts.Where(a => !a.IsDeleted))
        {
            alert.Status = AlertStatusEnum.Resolved;
            alert.ResolvedAt = now;
            _uow.Alerts.UpdateAsync(alert);
        }

        await _outbox.WriteAsync(new EnvironmentalIncidentResolvedEvent(
            IncidentId: incident.Id,
            SiteId: incident.SiteId,
            ResolvedAt: now,
            ResolvedByUserId: request.FalseAlarmBy,
            WasFalseAlarm: true,
            ResolutionNote: request.FalseAlarmReason), cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new EnvironmentalIncidentResponse { IsSuccess = true, StatusCode = 200, Data = ReportEnvironmentalIncidentCommandHandler.Map(incident) };
    }
}
