using BatteryService.Application.Anomaly;
using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using AlertEntity = BatteryService.Domain.Entities.Alert;

namespace BatteryService.Application.CQRS.Handler.Ambient;

public class BatchIngestAmbientReadingsCommandHandler
    : IRequestHandler<BatchIngestAmbientReadingsCommand, CommonResponse<int>>
{
    // Sprint Bonus NS-21 (#661) — dedup alert ambient site-level (ambient ~1 phút/mẫu, không dedup
    // thì site vượt ngưỡng 1h → 60 alert). Cùng cửa sổ với environmental incident (1h).
    private static readonly TimeSpan AlertDedupWindow = TimeSpan.FromHours(1);

    private readonly IBatteryUnitOfWork _uow;

    public BatchIngestAmbientReadingsCommandHandler(IBatteryUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<int>> Handle(BatchIngestAmbientReadingsCommand request, CancellationToken cancellationToken)
    {
        var readings = new List<AmbientReading>();
        foreach (var x in request.Items)
        {
            var reading = new AmbientReading
            {
                Time = x.Time.Kind == DateTimeKind.Utc ? x.Time : x.Time.ToUniversalTime(),
                SiteId = x.SiteId,
                AmbientTemperature = x.AmbientTemperature,
                Humidity = x.Humidity,
                SolarIrradiance = x.SolarIrradiance,
                Source = x.Source,
                SourceDeviceId = x.SourceDeviceId
            };
            readings.Add(reading);
            await _uow.AmbientReadings.AddAsync(reading);
        }

        await DetectAmbientAnomaliesAsync(readings, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new CommonResponse<int>
        {
            IsSuccess = true,
            StatusCode = 201,
            Data = request.Items.Count
        };
    }

    /// <summary>
    /// Sprint Bonus NS-21 (#661, E1) — wire <see cref="AnomalyRules.DetectAmbient"/> vào ingest.
    /// Trước fix, <c>AmbientThresholdConfig</c> có endpoint upsert nhưng KHÔNG ai dùng để phát hiện →
    /// nhà kho 45°C + ẩm 95% (combo thoát nhiệt pin) hệ thống im lặng. Detect-at-ingest: latency thấp,
    /// ambient 1 phút/mẫu nên không nặng. Tạo Alert site-level (BatteryAssetId=null) như environmental.
    /// </summary>
    private async Task DetectAmbientAnomaliesAsync(
        IReadOnlyList<AmbientReading> readings, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var siteIds = readings.Select(r => r.SiteId).Distinct().ToList();

        var configs = await _uow.AmbientThresholdConfigs.GetAllAsync()
            .Where(c => !c.IsDeleted && c.Enabled && siteIds.Contains(c.SiteId))
            .ToListAsync(ct);
        var configBySite = configs.ToDictionary(c => c.SiteId);
        if (configBySite.Count == 0)
            return;

        // Dedup trong 1 batch (nhiều reading cùng site vượt ngưỡng → 1 alert/loại).
        var createdKeys = new HashSet<(Guid SiteId, AnomalyTypeEnum Type)>();

        foreach (var reading in readings)
        {
            if (!configBySite.TryGetValue(reading.SiteId, out var config))
                continue;

            foreach (var anomaly in AnomalyRules.DetectAmbient(reading, config))
            {
                var key = (reading.SiteId, anomaly.Type);
                if (!createdKeys.Add(key))
                    continue;

                // Dedup DB: đã có alert Open cùng (site, loại) còn trong cửa sổ → skip.
                var hasRecent = await _uow.Alerts.GetAllAsync()
                    .AnyAsync(a => !a.IsDeleted
                        && a.SiteId == reading.SiteId
                        && a.BatteryAssetId == null
                        && a.AnomalyType == anomaly.Type
                        && a.Status == AlertStatusEnum.Open
                        && a.DedupWindowEndUtc > now, ct);
                if (hasRecent)
                    continue;

                await _uow.Alerts.AddAsync(new AlertEntity
                {
                    Id = Guid.NewGuid(),
                    SiteId = reading.SiteId,
                    BatteryAssetId = null,
                    AnomalyType = anomaly.Type,
                    Severity = anomaly.Severity,
                    ThresholdValue = anomaly.ThresholdValue,
                    ActualValue = anomaly.ActualValue,
                    Unit = anomaly.Unit,
                    DetectedAt = now,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = now.Add(AlertDedupWindow)
                });
            }
        }
    }
}
