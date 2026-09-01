using System.Text.Json;
using BatteryService.Application.Anomaly;
using BatteryService.Application.Services;
using BatteryService.Application.CQRS.Command.Ambient;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using OutboxEntity = BatteryService.Domain.Entities.OutboxMessage;

namespace BatteryService.Application.CQRS.Handler.Ambient;

public class BatchIngestAmbientReadingsCommandHandler
    : IRequestHandler<BatchIngestAmbientReadingsCommand, CommonResponse<int>>
{
    private readonly IBatteryUnitOfWork _uow;
    private readonly AnomalyEngineOptions _options;
    private readonly IOutboxSignal _outboxSignal;

    public BatchIngestAmbientReadingsCommandHandler(
        IBatteryUnitOfWork uow,
        IOptions<AnomalyEngineOptions> options,
        IOutboxSignal outboxSignal)
    {
        _uow = uow;
        _options = options.Value;
        _outboxSignal = outboxSignal;
    }

    public async Task<CommonResponse<int>> Handle(BatchIngestAmbientReadingsCommand request, CancellationToken cancellationToken)
    {
        // GH-806 — thiết bị chỉ được ghi cho ĐÚNG site của nó, và site phải có thật.
        // Trước đây SiteId lấy thẳng từ body: thiết bị Site A ghi được cho Site B (201), còn site
        // không tồn tại thì rơi xuống DB và nổ lỗi khoá ngoại → 500.
        var requestedSites = request.Items.Select(x => x.SiteId).Distinct().ToList();
        var existingSites = await _uow.Sites.GetAllAsync()
            .Where(site => !site.IsDeleted && requestedSites.Contains(site.Id))
            .Select(site => site.Id)
            .ToListAsync(cancellationToken);

        var access = IotSiteAccessGuard.Check(request.AuthenticatedDeviceSiteId, requestedSites, existingSites);
        if (!access.Allowed)
        {
            return new CommonResponse<int>
            {
                IsSuccess = false,
                StatusCode = access.StatusCode,
                Message = access.Message,
                Data = 0
            };
        }

        // Gas/nhiệt độ/nước cùng 1 thiết bị POST độc lập, mỗi lần 1 request riêng — nhưng NTP
        // isoNow() chỉ có độ phân giải giây, nên thỉnh thoảng 2 tick rơi đúng cùng 1 giây và
        // đụng khoá chính (time, site_id) → insert sau bị Postgres từ chối (23505), mất luôn
        // reading đó (water/gas/temp không có cơ chế retry như incident Flood). Vì firmware gọi
        // HTTP tuần tự/blocking trong 1 loop(), request đến sau LUÔN thấy request trước đã commit
        // — nên tra trước rồi gộp field non-null vào dòng đã có, thay vì insert mù.
        var readings = new List<AmbientReading>();
        foreach (var x in request.Items)
        {
            var time = x.Time.Kind == DateTimeKind.Utc ? x.Time : x.Time.ToUniversalTime();
            var existing = await _uow.AmbientReadings.GetAllAsync()
                .FirstOrDefaultAsync(r => r.Time == time && r.SiteId == x.SiteId, cancellationToken);

            if (existing is not null)
            {
                existing.AmbientTemperature ??= x.AmbientTemperature;
                existing.Humidity ??= x.Humidity;
                existing.SolarIrradiance ??= x.SolarIrradiance;
                existing.GasConcentration ??= x.GasConcentration;
                existing.WaterLeakDetected ??= x.WaterLeakDetected;
                readings.Add(existing);
                continue;
            }

            var reading = new AmbientReading
            {
                Time = time,
                SiteId = x.SiteId,
                AmbientTemperature = x.AmbientTemperature,
                Humidity = x.Humidity,
                SolarIrradiance = x.SolarIrradiance,
                GasConcentration = x.GasConcentration,
                WaterLeakDetected = x.WaterLeakDetected,
                Source = x.Source,
                SourceDeviceId = x.SourceDeviceId
            };
            readings.Add(reading);
            await _uow.AmbientReadings.AddAsync(reading);
        }

        var raisedAlerts = await DetectAmbientAnomaliesAsync(readings, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        // Đẩy event đi NGAY thay vì đợi hết tick relay (5 s) — với cảnh báo môi trường thì 5 s đó
        // là phần chờ lớn nhất còn lại: ngưỡng đã chấm xong ngay trong chính request này.
        //
        // Chỉ báo khi lượt này THỰC SỰ sinh alert: gói ambient về đều đặn mỗi 15 s, đánh thức
        // relay ở mọi gói là bắt nó quét rỗng suốt ngày mà không nhanh thêm được gì.
        if (raisedAlerts)
            _outboxSignal.Notify();

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
    /// <returns>
    /// <c>true</c> nếu lượt này có ghi ít nhất một event vào outbox — caller dùng để đánh thức
    /// relay ngay thay vì đợi hết tick.
    /// </returns>
    private async Task<bool> DetectAmbientAnomaliesAsync(
        IReadOnlyList<AmbientReading> readings, CancellationToken ct)
    {
        var wroteEvent = false;
        var now = DateTime.UtcNow;
        var siteIds = readings.Select(r => r.SiteId).Distinct().ToList();

        var configs = await _uow.AmbientThresholdConfigs.GetAllAsync()
            .Where(c => !c.IsDeleted && c.Enabled && siteIds.Contains(c.SiteId))
            .ToListAsync(ct);
        var configBySite = configs.ToDictionary(c => c.SiteId);
        if (configBySite.Count == 0)
            return wroteEvent;

        // Cần CustomerId + tên site để dựng event khởi tạo saga (alert site-level không có asset).
        var siteById = await _uow.Sites.GetAllAsync()
            .Where(s => !s.IsDeleted && siteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.CustomerId, s.Name })
            .ToDictionaryAsync(s => s.Id, ct);

        // Cùng cơ chế với AnomalyDetectionService (battery): MỖI lần phần cứng vượt ngưỡng đều ghi
        // một dòng alert. Lần đầu trong cửa sổ dedup là `Open` (bắn event); các lần sau ghi `Merged`
        // trỏ về alert cha — nhờ vậy bảng alert phản ánh đúng nhịp cảm biến thay vì im lặng 30 phút,
        // mà vẫn chỉ đẻ 1 ticket cho mỗi sự cố.
        var dedupWindow = TimeSpan.FromMinutes(_options.DedupWindowMinutes);
        var pendingAlerts = new Dictionary<(Guid SiteId, AnomalyTypeEnum Type), AlertEntity>();

        foreach (var reading in readings)
        {
            if (!configBySite.TryGetValue(reading.SiteId, out var config))
                continue;

            foreach (var anomaly in AnomalyRules.DetectAmbient(reading, config))
            {
                var key = (reading.SiteId, anomaly.Type);

                // Alert cha phải có mức nghiêm trọng KHÔNG THẤP HƠN alert mới (IOT3-106/M4): thiếu
                // điều kiện này thì một Warning đang mở sẽ nuốt trọn Critical theo sau — gas chớm
                // ngưỡng lúc 10:00 rồi vọt lên 64% lúc 10:05 sẽ vào DB dạng Merged, không event,
                // không ticket, không ai được gọi.
                AlertEntity? parent = null;
                if (pendingAlerts.TryGetValue(key, out var pendingParent)
                    && pendingParent.DedupWindowEndUtc > now
                    && pendingParent.Severity >= anomaly.Severity)
                {
                    parent = pendingParent;
                }
                else
                {
                    parent = await _uow.Alerts.GetAllAsync()
                        .Where(a => !a.IsDeleted
                            && a.SiteId == reading.SiteId
                            && a.BatteryAssetId == null
                            && a.AnomalyType == anomaly.Type
                            && a.Severity >= anomaly.Severity
                            && (a.Status == AlertStatusEnum.Open || a.Status == AlertStatusEnum.Acknowledged)
                            && a.DedupWindowEndUtc > now)
                        .OrderByDescending(a => a.DetectedAt)
                        .FirstOrDefaultAsync(ct);
                }

                if (parent is not null)
                {
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
                        DetectedAt = reading.Time,
                        Status = AlertStatusEnum.Merged,
                        MergedIntoAlertId = parent.Id,
                        DedupWindowEndUtc = parent.DedupWindowEndUtc
                    });
                    continue;
                }

                var alert = new AlertEntity
                {
                    Id = Guid.NewGuid(),
                    SiteId = reading.SiteId,
                    BatteryAssetId = null,
                    AnomalyType = anomaly.Type,
                    Severity = anomaly.Severity,
                    ThresholdValue = anomaly.ThresholdValue,
                    ActualValue = anomaly.ActualValue,
                    Unit = anomaly.Unit,
                    DetectedAt = reading.Time,
                    Status = AlertStatusEnum.Open,
                    DedupWindowEndUtc = reading.Time.Add(dedupWindow)
                };
                await _uow.Alerts.AddAsync(alert);
                pendingAlerts[key] = alert;

                // Alert ambient trước đây CHỈ nằm lại trong bảng alerts: không ai publish event nên
                // AlertTicketSaga không bao giờ chạy → nhà kho 85°C / gas 64% vẫn không có ticket nào,
                // dù TicketService đã map sẵn "HighAmbientTemp"/"HighHumidity" sang category + Site scope.
                // Bù đúng bước còn thiếu, theo cùng quy ước với AnomalyDetectionService: chỉ Critical mới
                // đẻ ticket (Warning chỉ để hiển thị/notify — cảnh báo nhẹ mà sinh ticket là spam).
                if (!siteById.TryGetValue(reading.SiteId, out var site))
                    continue;

                // Alert ambient truoc day KHONG BAO GIO ra notification. Khi saga bat (mac dinh),
                // TicketService bo dang ky consumer V1, nen hai event chia vai ro rang:
                //   V1  `BatteryAnomalyDetectedEvent`         -> NotificationService
                //   V2  `BatteryAnomalyDetectedV2Event`       -> AlertTicketSaga (tao ticket)
                //   Warning `BatteryAnomalyWarningDetectedEvent` -> NotificationService
                // Duong nay chi phat V2, nen Critical co ticket ma khong ai duoc bao; con Warning
                // thi `continue` truoc khi kip phat gi ca. Do la vi sao incident do thiet bi tu bao
                // (khoi/ro khi/ngap) co noti con "nhiet do moi truong cao" thi im lang tuyet doi.
                //
                // Khong can dedup rieng cho Warning nhu ben AnomalyDetectionService: o day alert
                // trung trong cua so dedup da thanh `Merged` va `continue` phia tren, nen chi alert
                // `Open` dau tien moi toi duoc doan nay.
                if (anomaly.Severity != AlertSeverityEnum.Critical)
                {
                    if (!_options.PublishWarningNotifications)
                        continue;

                    var warningEvt = new BatteryAnomalyWarningDetectedEvent(
                        AlertId: alert.Id,
                        BatteryAssetId: null,
                        CustomerId: site.CustomerId,
                        AssetSerialNumber: site.Name,
                        AnomalyType: (int)alert.AnomalyType,
                        Severity: (int)alert.Severity,
                        ThresholdValue: alert.ThresholdValue,
                        ActualValue: alert.ActualValue,
                        Unit: alert.Unit,
                        DetectedAt: alert.DetectedAt,
                        AnomalyTypeName: alert.AnomalyType.ToString(),
                        SeverityName: alert.Severity.ToString());

                    wroteEvent = true;
                await _uow.OutboxMessages.AddAsync(new OutboxEntity
                    {
                        Id = Guid.NewGuid(),
                        AggregateId = alert.Id,
                        Type = nameof(BatteryAnomalyWarningDetectedEvent),
                        Payload = JsonSerializer.Serialize(warningEvt),
                        OccurredAtUtc = now
                    });
                    continue;
                }

                // Critical: V1 cho notification. `BatteryAssetId = Guid.Empty` vi day la su co cap
                // site, khong thuoc vien pin nao — cung quy uoc `?? Guid.Empty` ma duong battery dung.
                var notifyEvt = new BatteryAnomalyDetectedEvent(
                    AlertId: alert.Id,
                    BatteryAssetId: Guid.Empty,
                    CustomerId: site.CustomerId,
                    AssetSerialNumber: site.Name,
                    AnomalyType: (int)alert.AnomalyType,
                    Severity: (int)alert.Severity,
                    ThresholdValue: alert.ThresholdValue,
                    ActualValue: alert.ActualValue,
                    Unit: alert.Unit,
                    DetectedAt: alert.DetectedAt,
                    AnomalyTypeName: alert.AnomalyType.ToString(),
                    SeverityName: alert.Severity.ToString());

                wroteEvent = true;
                await _uow.OutboxMessages.AddAsync(new OutboxEntity
                {
                    Id = Guid.NewGuid(),
                    AggregateId = alert.Id,
                    Type = nameof(BatteryAnomalyDetectedEvent),
                    Payload = JsonSerializer.Serialize(notifyEvt),
                    OccurredAtUtc = now
                });

                // V2 là event DUY NHẤT khởi tạo saga, và là bản được thiết kế cho scope site-level
                // (BatteryAssetId/AssetSerialNumber nullable) — đúng hình dạng của alert môi trường.
                var evt = new BatteryAnomalyDetectedV2Event(
                    AlertId: alert.Id,
                    BatteryAssetId: null,
                    CustomerId: site.CustomerId,
                    SiteId: reading.SiteId,
                    AssetSerialNumber: site.Name,
                    AnomalyType: (int)alert.AnomalyType,
                    Severity: (int)alert.Severity,
                    ThresholdValue: alert.ThresholdValue ?? 0m,
                    ActualValue: alert.ActualValue ?? 0m,
                    Unit: alert.Unit ?? string.Empty,
                    DetectedAt: alert.DetectedAt,
                    InternalResistanceMilliohm: null,
                    CellVoltageDeltaMv: null,
                    EnvironmentalIncidentId: null);

                wroteEvent = true;
                await _uow.OutboxMessages.AddAsync(new OutboxEntity
                {
                    Id = Guid.NewGuid(),
                    AggregateId = alert.Id,
                    Type = nameof(BatteryAnomalyDetectedV2Event),
                    Payload = JsonSerializer.Serialize(evt),
                    OccurredAtUtc = now
                });
            }
        }

        return wroteEvent;
    }
}
