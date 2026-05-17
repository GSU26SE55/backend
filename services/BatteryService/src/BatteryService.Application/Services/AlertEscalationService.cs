using System.Text.Json;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using OutboxEntity = BatteryService.Domain.Entities.OutboxMessage;

namespace BatteryService.Application.Services;

public class AlertEscalationService : IAlertEscalationService
{
    private const string EscalationEventType = "BatteryAnomalyEscalatedEvent";

    private readonly IBatteryUnitOfWork _unitOfWork;

    public AlertEscalationService(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<AlertEscalationResult> EscalateAsync(
        int minutesUntilEscalate, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(minutesUntilEscalate);
        var result = new AlertEscalationResult();

        var staleAlerts = await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.Status == AlertStatusEnum.Open
                        && a.Severity == AlertSeverityEnum.Critical
                        && a.DetectedAt <= cutoff)
            .Include(a => a.BatteryAsset)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (staleAlerts.Count == 0)
            return result;

        var alertIds = staleAlerts.Select(a => a.Id).ToList();
        var alreadyEscalatedIds = await _unitOfWork.OutboxMessages
            .GetAllAsync()
            .Where(m => alertIds.Contains(m.AggregateId) && m.Type == EscalationEventType)
            .Select(m => m.AggregateId)
            .ToListAsync(cancellationToken);
        var escalatedSet = new HashSet<Guid>(alreadyEscalatedIds);

        foreach (var alert in staleAlerts)
        {
            if (escalatedSet.Contains(alert.Id))
            {
                result.AlreadyEscalated++;
                continue;
            }

            var evt = new BatteryAnomalyDetectedEvent(
                AlertId: alert.Id,
                BatteryAssetId: alert.BatteryAssetId,
                CustomerId: alert.BatteryAsset?.CustomerId ?? Guid.Empty,
                AssetSerialNumber: alert.BatteryAsset?.SerialNumber ?? string.Empty,
                AnomalyType: (int)alert.AnomalyType,
                Severity: (int)alert.Severity,
                ThresholdValue: alert.ThresholdValue,
                ActualValue: alert.ActualValue,
                Unit: alert.Unit,
                DetectedAt: alert.DetectedAt);

            await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = alert.Id,
                Type = EscalationEventType,
                Payload = JsonSerializer.Serialize(evt),
                OccurredAtUtc = now
            });
            result.Escalated++;
        }

        if (result.Escalated > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }
}
