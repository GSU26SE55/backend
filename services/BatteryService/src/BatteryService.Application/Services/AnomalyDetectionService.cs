using System.Text.Json;
using BatteryService.Application.Anomaly;
using BatteryService.Application.Common;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Events;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using OutboxEntity = BatteryService.Domain.Entities.OutboxMessage;

namespace BatteryService.Application.Services;

/// <summary>
/// Logic anomaly detection — chạy bởi ThresholdCheckBackgroundService.
/// 1) Load readings trong cửa sổ lookback
/// 2) Detect từng reading qua <see cref="AnomalyRules"/>
/// 3) BR-03 dedup merge nếu có alert "anh em" còn trong dedup window
/// 4) Ghi Outbox event cho alert Critical (atomic với SaveChanges)
/// </summary>
public class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly AnomalyEngineOptions _options;

    public AnomalyDetectionService(
        IBatteryUnitOfWork unitOfWork,
        IOptions<AnomalyEngineOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<AnomalyDetectionResult> ScanRecentReadingsAsync(
        TimeSpan lookbackWindow, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - lookbackWindow;
        var dedupWindow = TimeSpan.FromMinutes(_options.DedupWindowMinutes);

        var readings = await _unitOfWork.SensorReadings
            .GetAllAsync()
            .Where(r => r.Time >= cutoff)
            .OrderBy(r => r.Time)
            .ToListAsync(cancellationToken);

        var result = new AnomalyDetectionResult { ReadingsScanned = readings.Count };
        if (readings.Count == 0)
            return result;

        var assetIds = readings.Select(r => r.BatteryAssetId).Distinct().ToList();
        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(a => assetIds.Contains(a.Id) && !a.IsDeleted)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var batteryTypeIds = assets.Values.Select(a => a.BatteryTypeId).Distinct().ToList();
        var thresholds = await _unitOfWork.ThresholdConfigs
            .GetAllAsync()
            .Where(t => batteryTypeIds.Contains(t.BatteryTypeId) && t.IsActive && !t.IsDeleted)
            .ToDictionaryAsync(t => t.BatteryTypeId, cancellationToken);

        // IOT3-106/M4 — alert MỚI TẠO trong chính lượt quét này, chưa `SaveChanges`.
        //
        // `FindActiveAlertToMergeAsync` hỏi DB bằng `.FirstOrDefaultAsync()`, nên nó KHÔNG thấy
        // những alert vừa `AddAsync` còn nằm trong change tracker. Hệ quả đo được: 6 reading vi
        // phạm cách nhau 2 giây, rơi cùng một lượt quét → 5 alert `Open` TRÙNG NHAU cho cùng một
        // pin, `merged_into_alert_id` toàn NULL. Người trực nhận 5 cảnh báo cho MỘT sự cố.
        //
        // Lỗi này chỉ lộ khi DB SẠCH: còn alert cũ trong `DedupWindowEndUtc` thì reading đầu tìm
        // thấy cha ngay và mọi alert sau đều thành `Merged` — nhìn như dedup hoàn hảo.
        //
        // Đây đúng cơ chế mà `ShouldSuppressByNoiseAsync` đã lường trước cho `noise_breach_events`
        // ("row pending không được DB đếm"); chỗ này thì chưa.
        var pendingAlerts = new Dictionary<(Guid AssetId, AnomalyTypeEnum Type), AlertEntity>();

        foreach (var reading in readings)
        {
            // Sprint Bonus NS-08 (#652, N4) — chỉ Detect trên reading primary. Reading redundant
            // (INA226 real mode: temp=0, SOC=0) / external-temp (mirror) đi qua threshold check sẽ
            // sinh LowSoc Critical giả spam mọi pin; chúng chỉ dùng cho cross-source validation.
            if (!SensorSource.IsPrimary(reading.SensorSourceCode))
                continue;

            if (!assets.TryGetValue(reading.BatteryAssetId, out var asset))
                continue;
            if (!thresholds.TryGetValue(asset.BatteryTypeId, out var threshold))
                continue;

            var anomalies = AnomalyRules.Detect(reading, threshold);
            if (anomalies.Count == 0)
                continue;

            foreach (var anomaly in anomalies)
            {
                // The background lookback deliberately overlaps two ticks. A reading that has
                // already produced either an Open or Merged alert must therefore be ignored on
                // the next scan; otherwise every overlap creates another meaningless Merged row.
                var readingAlreadyProcessed = await _unitOfWork.Alerts.GetAllAsync()
                    .AnyAsync(a => !a.IsDeleted
                                   && a.BatteryAssetId == reading.BatteryAssetId
                                   && a.AnomalyType == anomaly.Type
                                   && a.DetectedAt == reading.Time,
                        cancellationToken);
                if (readingAlreadyProcessed)
                    continue;

                // Sprint 5B B1 (#152) — Noise suppression frequency-based.
                // Bypass: EnvironmentalIncident và Critical Overheat (an toàn).
                var (suppress, recordedBreach) = await ShouldSuppressByNoiseAsync(
                    reading, anomaly, threshold, cancellationToken);
                if (suppress)
                {
                    result.AlertsSuppressed++;
                    continue;
                }

                // IOT3-106/M4 — tra phần CHƯA LƯU trước, rồi mới hỏi DB.
                var key = (reading.BatteryAssetId, anomaly.Type);
                AlertEntity? existing = null;
                if (pendingAlerts.TryGetValue(key, out var pendingParent)
                    && pendingParent.DedupWindowEndUtc > now)
                {
                    existing = pendingParent;
                }
                else
                {
                    existing = await FindActiveAlertToMergeAsync(
                        reading.BatteryAssetId, anomaly.Type, now, cancellationToken);
                }

                if (existing is not null)
                {
                    await _unitOfWork.Alerts.AddAsync(new AlertEntity
                    {
                        Id = Guid.NewGuid(),
                        BatteryAssetId = reading.BatteryAssetId,
                        AnomalyType = anomaly.Type,
                        Severity = anomaly.Severity,
                        ThresholdValue = anomaly.ThresholdValue,
                        ActualValue = anomaly.ActualValue,
                        Unit = anomaly.Unit,
                        DetectedAt = reading.Time,
                        Status = AlertStatusEnum.Merged,
                        MergedIntoAlertId = existing.Id,
                        DedupWindowEndUtc = existing.DedupWindowEndUtc
                    });
                    result.AlertsMerged++;
                    continue;
                }

                var alert = new AlertEntity
                {
                    Id = Guid.NewGuid(),
                    BatteryAssetId = reading.BatteryAssetId,
                    AnomalyType = anomaly.Type,
                    Severity = anomaly.Severity,
                    ThresholdValue = anomaly.ThresholdValue,
                    ActualValue = anomaly.ActualValue,
                    Unit = anomaly.Unit,
                    DetectedAt = reading.Time,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = reading.Time.Add(dedupWindow)
                };
                await _unitOfWork.Alerts.AddAsync(alert);
                result.AlertsCreated++;

                // IOT3-106/M4 — ghi vào từ điển để những reading SAU trong cùng lượt quét gộp được
                // vào alert này thay vì tạo thêm alert `Open` trùng. Ghi đè bản cũ nếu có: reading
                // xử lý sau luôn có `DedupWindowEndUtc` mới hơn hoặc bằng, nên giữ bản mới là đúng.
                pendingAlerts[key] = alert;

                // Sprint Bonus NS-10 (#654, N2) — alert nổ từ chuỗi breach suppression → link
                // chuỗi vào alert (audit "alert này nổ từ chuỗi breach nào") + giữ khỏi retention.
                //
                // IOT3-106/M3 — điều kiện gác cũ là `if (recordedBreach is not null)`, và nó khiến
                // đường này KHÔNG BAO GIỜ CHẠY. Đo được: `promoted_to_alert_id` NULL trên 0/11 bản
                // ghi toàn bảng.
                //
                // Lý do: `ShouldSuppressByNoiseAsync` trả `recorded = null` khi breach của reading
                // này ĐÃ được ghi ở lượt quét trước (`alreadyRecorded == true`). Mà alert của đường
                // chống nhiễu CHỈ nổ ở lượt quét lại — lượt đầu `effectiveCount = breachCount + 1`
                // chưa đạt `NoiseSuppressionCount` nên luôn bị chặn. Hai điều kiện loại trừ nhau:
                // lượt nào có `recordedBreach` thì không nổ alert; lượt nào nổ alert thì nó đã null.
                //
                // Gác đúng phải là "alert này có đi qua đường chống nhiễu không", tức xét chính
                // `threshold`. `PromoteBreachChainAsync` tự truy vấn cả chuỗi từ DB nên không cần
                // `recordedBreach` để làm việc — tham số đó giờ nullable, chỉ dùng để xử lý riêng
                // row còn pending (DB query không thấy row chưa SaveChanges).
                if (threshold.NoiseSuppressionEnabled && threshold.NoiseSuppressionCount > 1)
                {
                    await PromoteBreachChainAsync(
                        reading.BatteryAssetId, anomaly.Type, threshold, alert.Id,
                        recordedBreach, cancellationToken);
                }

                if (anomaly.Severity == AlertSeverityEnum.Critical)
                {
                    var evt = new BatteryAnomalyDetectedEvent(
                        AlertId: alert.Id,
                        BatteryAssetId: alert.BatteryAssetId ?? Guid.Empty,
                        CustomerId: asset.CustomerId,
                        AssetSerialNumber: asset.SerialNumber,
                        AnomalyType: (int)alert.AnomalyType,
                        Severity: (int)alert.Severity,
                        ThresholdValue: alert.ThresholdValue,
                        ActualValue: alert.ActualValue,
                        Unit: alert.Unit,
                        DetectedAt: alert.DetectedAt,
                        // Gửi kèm TÊN enum: subscriber không tham chiếu được hai enum này của
                        // BatteryService nên chỉ có số thì không dựng nổi câu chữ cho người đọc.
                        AnomalyTypeName: alert.AnomalyType.ToString(),
                        SeverityName: alert.Severity.ToString());
                    await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
                    {
                        Id = Guid.NewGuid(),
                        AggregateId = alert.Id,
                        Type = nameof(BatteryAnomalyDetectedEvent),
                        Payload = JsonSerializer.Serialize(evt),
                        OccurredAtUtc = now
                    });

                    // Sprint IoT-2 #IoT2-30 — V2 event với SiteId + Tier-2 fields.
                    var v2 = new BatteryAnomalyDetectedV2Event(
                        AlertId: alert.Id,
                        BatteryAssetId: alert.BatteryAssetId,
                        CustomerId: asset.CustomerId,
                        SiteId: asset.SiteId,
                        AssetSerialNumber: asset.SerialNumber,
                        AnomalyType: (int)alert.AnomalyType,
                        Severity: (int)alert.Severity,
                        ThresholdValue: alert.ThresholdValue ?? 0m,
                        ActualValue: alert.ActualValue ?? 0m,
                        Unit: alert.Unit ?? string.Empty,
                        DetectedAt: alert.DetectedAt,
                        InternalResistanceMilliohm: reading.InternalResistanceMilliohm,
                        CellVoltageDeltaMv: reading.CellVoltageDeltaMv,
                        EnvironmentalIncidentId: alert.EnvironmentalIncidentId);
                    await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
                    {
                        Id = Guid.NewGuid(),
                        AggregateId = alert.Id,
                        Type = nameof(BatteryAnomalyDetectedV2Event),
                        Payload = JsonSerializer.Serialize(v2),
                        OccurredAtUtc = now
                    });
                    result.OutboxEventsQueued++;
                }
                else if (_options.PublishWarningNotifications)
                {
                    // Sprint 6.2 NOTI-08 (#679) — alert Warning/Info: publish event RIÊNG chỉ dành cho
                    // NotificationService (spec §3.4 T#11 Info → InApp, T#12 Warning → InApp+Push).
                    // KHÔNG dùng BatteryAnomalyDetectedEvent vì TicketService consume nó để auto-tạo
                    // ticket — cảnh báo nhẹ mà đẻ ticket đúng là điều team đang né bằng cách im lặng.
                    if (await ShouldNotifyWarningAsync(alert.BatteryAssetId, alert.AnomalyType, now, cancellationToken))
                    {
                        var warningEvt = new BatteryAnomalyWarningDetectedEvent(
                            AlertId: alert.Id,
                            BatteryAssetId: alert.BatteryAssetId,
                            CustomerId: asset.CustomerId,
                            AssetSerialNumber: asset.SerialNumber,
                            AnomalyType: (int)alert.AnomalyType,
                            Severity: (int)alert.Severity,
                            ThresholdValue: alert.ThresholdValue,
                            ActualValue: alert.ActualValue,
                            Unit: alert.Unit,
                            DetectedAt: alert.DetectedAt,
                            AnomalyTypeName: alert.AnomalyType.ToString(),
                            SeverityName: alert.Severity.ToString());

                        await _unitOfWork.OutboxMessages.AddAsync(new OutboxEntity
                        {
                            Id = Guid.NewGuid(),
                            AggregateId = alert.Id,
                            Type = nameof(BatteryAnomalyWarningDetectedEvent),
                            Payload = JsonSerializer.Serialize(warningEvt),
                            OccurredAtUtc = now
                        });
                        result.OutboxEventsQueued++;
                    }
                }
            }
        }

        // Sprint Bonus NS-11 (#655, N6) — đường SensorMismatch B10 (#157) đã HỢP NHẤT về
        // CrossSourceValidationService (#IoT2-28): 1 nơi duy nhất ghép cặp Bms↔IotGateway,
        // dedup 15' và tạo alert. Ngưỡng dùng chung AnomalyRules.SensorMismatch*.

        // Sprint Bonus NS-07 (#651, N1) — AlertsSuppressed PHẢI nằm trong điều kiện save:
        // tick chỉ toàn anomaly bị nén vẫn phải persist NoiseBreachEvent pending; nếu không,
        // scope DI mới mỗi tick vứt breach event → count mãi = 0 → suppression chặn alert vĩnh viễn.
        if (result.AlertsCreated + result.AlertsMerged + result.AlertsSuppressed > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    /// <summary>
    /// Sprint 6.2 NOTI-08 (#679) — chống spam notify Warning/Info.
    ///
    /// Alert Warning không đi qua dedup-merge như Critical (mỗi tick vượt ngưỡng có thể tạo alert
    /// mới), nên nếu publish thẳng thì Customer lãnh một trận thông báo. Chỉ publish khi trong
    /// <c>WarningNotifyDedupMinutes</c> gần nhất CHƯA có outbox event cùng
    /// (BatteryAssetId × AnomalyType).
    /// </summary>
    private async Task<bool> ShouldNotifyWarningAsync(
        Guid? batteryAssetId, AnomalyTypeEnum anomalyType, DateTime now, CancellationToken ct)
    {
        if (batteryAssetId is null || batteryAssetId == Guid.Empty)
            return false;

        var window = TimeSpan.FromMinutes(Math.Max(0, _options.WarningNotifyDedupMinutes));
        if (window <= TimeSpan.Zero)
            return true;

        var cutoff = now - window;
        var eventType = nameof(BatteryAnomalyWarningDetectedEvent);

        // Outbox lưu AggregateId = AlertId nên phải join ngược qua alerts để lọc theo asset + type.
        var recentAlertIds = await _unitOfWork.Alerts.GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.BatteryAssetId == batteryAssetId
                        && a.AnomalyType == anomalyType
                        && a.DetectedAt >= cutoff)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (recentAlertIds.Count == 0)
            return true;

        var alreadyNotified = await _unitOfWork.OutboxMessages.GetAllAsync()
            .AnyAsync(o => o.Type == eventType && recentAlertIds.Contains(o.AggregateId), ct);

        return !alreadyNotified;
    }

    private async Task<AlertEntity?> FindActiveAlertToMergeAsync(
        Guid batteryAssetId, AnomalyTypeEnum anomalyType, DateTime now, CancellationToken ct)
    {
        return await _unitOfWork.Alerts
            .GetAllAsync()
            .Where(a => !a.IsDeleted
                        && a.BatteryAssetId == batteryAssetId
                        && a.AnomalyType == anomalyType
                        && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged)
                        && a.DedupWindowEndUtc > now)
            .OrderByDescending(a => a.DetectedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Sprint 5B B1 (#152) — Noise suppression frequency-based.
    /// Ghi <c>NoiseBreachEvent</c> để track + chỉ raise Alert khi vi phạm
    /// >= <c>NoiseSuppressionCount</c> lần trong <c>NoiseSuppressionWindowHours</c> giờ.
    ///
    /// Bypass khi:
    /// - Threshold không bật noise suppression (return false → không suppress).
    /// - Anomaly Critical Overheat (an toàn — fire alert ngay).
    /// - Anomaly EnvironmentalIncident (site-level — bypass).
    ///
    /// Sprint Bonus NS-10 (#654, N3): breach ghi theo <c>reading.Time</c> + dedup theo
    /// (assetId, anomalyType, reading.Time) — scan lookback overlap 2× không được đếm
    /// cùng 1 reading thành 2 breach; copy <c>SourceType</c> từ reading (B9).
    /// Trả kèm breach vừa ghi (nếu có) để caller promote khi alert nổ (N2).
    /// </summary>
    private async Task<(bool Suppress, Domain.Entities.NoiseBreachEvent? RecordedBreach)> ShouldSuppressByNoiseAsync(
        Domain.Entities.SensorReading reading,
        Anomaly.AnomalyDetection anomaly,
        Domain.Entities.ThresholdConfig threshold,
        CancellationToken ct)
    {
        // Bypass — luôn fire alert ngay.
        if (anomaly.Type == AnomalyTypeEnum.EnvironmentalIncident)
            return (false, null);
        if (anomaly.Type == AnomalyTypeEnum.Overheat && anomaly.Severity == AlertSeverityEnum.Critical)
            return (false, null);

        // §1.3.3 — Count/Window NOT NULL với default, chỉ cần check Enabled + Count > 1.
        if (!threshold.NoiseSuppressionEnabled || threshold.NoiseSuppressionCount <= 1)
            return (false, null);

        var batteryAssetId = reading.BatteryAssetId;

        // Đếm breach đã persist trong window TRƯỚC khi Add (row pending không được DB đếm).
        var windowCutoff = DateTime.UtcNow.AddHours(-threshold.NoiseSuppressionWindowHours);
        var breachCount = await _unitOfWork.NoiseBreachEvents.GetAllAsync()
            .CountAsync(n => n.BatteryAssetId == batteryAssetId
                          && n.AnomalyType == anomaly.Type
                          && n.Time >= windowCutoff, ct);

        // NS-10 (N3) — dedup: cùng 1 reading bị scan lại ở tick sau (lookback overlap 2×)
        // đã có breach ghi theo đúng reading.Time → không ghi đôi, không đếm lạm phát.
        var alreadyRecorded = await _unitOfWork.NoiseBreachEvents.GetAllAsync()
            .AnyAsync(n => n.BatteryAssetId == batteryAssetId
                        && n.AnomalyType == anomaly.Type
                        && n.Time == reading.Time, ct);

        Domain.Entities.NoiseBreachEvent? recorded = null;
        if (!alreadyRecorded)
        {
            recorded = new Domain.Entities.NoiseBreachEvent
            {
                Time = reading.Time,
                BatteryAssetId = batteryAssetId,
                AnomalyType = anomaly.Type,
                ThresholdValue = anomaly.ThresholdValue,
                ActualValue = anomaly.ActualValue,
                Unit = anomaly.Unit,
                SourceType = reading.SourceType
            };
            await _unitOfWork.NoiseBreachEvents.AddAsync(recorded);
        }

        // Breach của reading này (persisted nếu re-scan, pending nếu mới) + breach persisted khác.
        var effectiveCount = breachCount + (alreadyRecorded ? 0 : 1);
        return (effectiveCount < threshold.NoiseSuppressionCount, recorded);
    }

    /// <summary>
    /// Sprint Bonus NS-10 (#654, N2) — khi alert nổ qua đường suppression: gán
    /// <c>PromotedToAlertId</c> cho chuỗi breach cùng (assetId, anomalyType) trong window
    /// (audit "alert này nổ từ chuỗi breach nào") — retention sẽ giữ các row đã promote.
    /// </summary>
    private async Task PromoteBreachChainAsync(
        Guid batteryAssetId,
        AnomalyTypeEnum anomalyType,
        Domain.Entities.ThresholdConfig threshold,
        Guid alertId,
        Domain.Entities.NoiseBreachEvent? pendingBreach,
        CancellationToken ct)
    {
        var windowCutoff = DateTime.UtcNow.AddHours(-threshold.NoiseSuppressionWindowHours);
        var chain = await _unitOfWork.NoiseBreachEvents.GetAllAsync()
            .Where(n => n.BatteryAssetId == batteryAssetId
                     && n.AnomalyType == anomalyType
                     && n.Time >= windowCutoff
                     && n.PromotedToAlertId == null)
            .ToListAsync(ct);

        foreach (var breach in chain)
        {
            if (pendingBreach is not null && ReferenceEquals(breach, pendingBreach))
                continue; // pending set trực tiếp bên dưới (DB query không thấy row pending)
            breach.PromotedToAlertId = alertId;
            _unitOfWork.NoiseBreachEvents.UpdateAsync(breach);
        }

        // IOT3-106/M3 — `pendingBreach` null là trường hợp THƯỜNG GẶP NHẤT, không phải ngoại lệ:
        // alert nổ ở lượt quét lại thì breach của reading này đã persisted từ lượt trước và đã nằm
        // trong `chain` ở trên rồi.
        if (pendingBreach is not null)
            pendingBreach.PromotedToAlertId = alertId;
    }
}
