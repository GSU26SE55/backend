using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.DTOs.Realtime;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using AlertEntity = BatteryService.Domain.Entities.Alert;
using IotDeviceEntity = BatteryService.Domain.Entities.IotDevice;
using SensorReadingEntity = BatteryService.Domain.Entities.SensorReading;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

/// <summary>
/// Sprint IoT-1 (#246, #247) + Sprint IoT-2 (#IoT2-16, #IoT2-17):
/// - Resolve BatteryAssetId từ serial nếu cần.
/// - Reject outlier sensor (hard-coded bounds — §1050 overall.md). Count vào device — &gt;50/h → auto-Decommissioned (#IoT2-17).
/// - Apply IotDeviceCalibration <c>raw * Scale + Offset</c> trước khi lưu.
/// - Update IotDevice.LastSeenAt.
/// - Idempotency: trùng <c>(DeviceCode, IdempotencyKey)</c> → trả response cũ, KHÔNG insert (#IoT2-16).
/// - Emit Prometheus counter (xem <c>IotMetrics</c>) qua interface <see cref="IIotMetricsRecorder"/>.
/// </summary>
public class BatchIngestSensorReadingsCommandHandler : IRequestHandler<BatchIngestSensorReadingsCommand, CommonResponse<SensorReadingBatchIngestResult>>
{
    // Outlier bounds — Sprint IoT-2 #IoT2-17 spec §52.5.
    private const decimal MaxVoltage = 1000m;       // spec: voltage > 1000V or < 0
    private const decimal MinTemperature = -50m;    // spec: temp ngoài [-50..150]
    private const decimal MaxTemperature = 150m;
    private const decimal MaxCurrent = 1000m;
    private const decimal MinSoc = 0m;              // spec: SOC ngoài [0..100]
    private const decimal MaxSoc = 100m;
    private const decimal MinSoh = 0m;              // spec: SOH ngoài [0..100]
    private const decimal MaxSoh = 100m;

    // Sprint IoT-2 #IoT2-15 — clock skew threshold (>5 phút → reject + metric).
    private const double ClockSkewMaxMinutes = 5;

    // Sprint IoT-2 #IoT2-17 — auto-disable threshold.
    private const int OutlierThresholdPerHour = 50;
    private static readonly TimeSpan OutlierWindow = TimeSpan.FromHours(1);

    // Sprint IoT-2 #IoT2-16 — idempotency TTL.
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IIotMetricsRecorder _metrics;
    private readonly IIotCalibrationCache _calibrationCache;
    private readonly ITelemetryPublisher _telemetryPublisher;
    private readonly ITelemetryStatsService _telemetryStatsService;
    private readonly ILogger<BatchIngestSensorReadingsCommandHandler> _logger;

    public BatchIngestSensorReadingsCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IIotMetricsRecorder metrics,
        IIotCalibrationCache calibrationCache,
        ITelemetryPublisher telemetryPublisher,
        ITelemetryStatsService telemetryStatsService,
        ILogger<BatchIngestSensorReadingsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _metrics = metrics;
        _calibrationCache = calibrationCache;
        _telemetryPublisher = telemetryPublisher;
        _telemetryStatsService = telemetryStatsService;
        _logger = logger;
    }

    public async Task<CommonResponse<SensorReadingBatchIngestResult>> Handle(BatchIngestSensorReadingsCommand request, CancellationToken cancellationToken)
    {
        // ─── Sprint IoT-2 #IoT2-16 — idempotency check ───
        if (!string.IsNullOrWhiteSpace(request.DeviceCode) && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var dup = await _unitOfWork.SensorIngestIdempotencyRecords.GetAllAsync()
                .Where(r => !r.IsDeleted
                            && r.DeviceCode == request.DeviceCode
                            && r.IdempotencyKey == request.IdempotencyKey
                            && r.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync(cancellationToken);
            if (dup is not null)
            {
                _metrics.RejectionRecorded("idempotency_replay");
                return new CommonResponse<SensorReadingBatchIngestResult>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Message = dup.Message ?? "Idempotent replay — trả response cũ.",
                    Data = new SensorReadingBatchIngestResult
                    {
                        TotalReceived = dup.TotalReceived,
                        Inserted = dup.Inserted,
                        Skipped = dup.Skipped
                    }
                };
            }
        }

        // ─── Sprint IoT-2 #IoT2-15 — clock skew pre-check ───
        // Spec §52.5: |deviceTimestamp - serverNow| > 5 phút → 400 + fire metric reason=clock_drift.
        // Check trong handler (KHÔNG trong ValidateAsync) để emit metric counter chính xác.
        var nowUtc = DateTime.UtcNow;
        var skewErrors = new List<Errors>();
        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            if (!item.DeviceTimestamp.HasValue)
                continue;
            var skewMin = Math.Abs((item.DeviceTimestamp.Value.ToUniversalTime() - nowUtc).TotalMinutes);
            if (skewMin > ClockSkewMaxMinutes)
            {
                _metrics.RejectionRecorded("clock_drift");
                skewErrors.Add(new Errors
                {
                    Field = $"Items[{i}].DeviceTimestamp",
                    Detail = $"Clock skew {skewMin:F1} phút > {ClockSkewMaxMinutes} phút. Đồng bộ NTP."
                });
            }
        }
        if (skewErrors.Count > 0)
        {
            var resp = new CommonResponse<SensorReadingBatchIngestResult>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Clock skew vượt ngưỡng — đồng bộ NTP trước khi gửi.",
                Data = new SensorReadingBatchIngestResult { TotalReceived = request.Items.Count }
            };
            foreach (var e in skewErrors)
                resp.ListErrors.Add(e);
            return resp;
        }

        // Resolve serial → assetId nếu cần.
        var serialsToResolve = request.Items
            .Where(i => i.BatteryAssetId == Guid.Empty && !string.IsNullOrWhiteSpace(i.BatteryAssetSerial))
            .Select(i => i.BatteryAssetSerial!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var serialMap = new Dictionary<string, Guid>();
        if (serialsToResolve.Count > 0)
        {
            serialMap = await _unitOfWork.BatteryAssets.GetAllAsync()
                .Where(a => !a.IsDeleted && serialsToResolve.Contains(a.SerialNumber))
                .ToDictionaryAsync(a => a.SerialNumber, a => a.Id, cancellationToken);
        }

        foreach (var item in request.Items)
        {
            if (item.BatteryAssetId == Guid.Empty && !string.IsNullOrWhiteSpace(item.BatteryAssetSerial))
            {
                var key = item.BatteryAssetSerial.Trim().ToUpperInvariant();
                if (serialMap.TryGetValue(key, out var resolvedId))
                    item.BatteryAssetId = resolvedId;
            }
        }

        var assetIds = request.Items
            .Select(item => item.BatteryAssetId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var assets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .Where(asset => assetIds.Contains(asset.Id) && !asset.IsDeleted)
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);

        // ─── Sprint IoT-2 #IoT2-18 — device permission check ───
        // Device chỉ được ingest cho pin cùng SiteId. Mismatch → 403 toàn batch.
        if (request.AuthenticatedDeviceId.HasValue && assets.Count > 0)
        {
            var deviceSiteId = await _unitOfWork.IotDevices.GetAllAsync()
                .Where(d => d.Id == request.AuthenticatedDeviceId.Value)
                .Select(d => (Guid?)d.SiteId)
                .FirstOrDefaultAsync(cancellationToken);

            if (deviceSiteId.HasValue)
            {
                var crossSiteAssets = assets.Values
                    .Where(a => a.SiteId != deviceSiteId.Value)
                    .ToList();
                if (crossSiteAssets.Count > 0)
                {
                    foreach (var _ in crossSiteAssets)
                        _metrics.RejectionRecorded("mapping_invalid");
                    _logger.LogWarning(
                        "Device {DeviceId} ingest bị reject — {Count} battery thuộc site khác",
                        request.AuthenticatedDeviceId.Value, crossSiteAssets.Count);
                    return new CommonResponse<SensorReadingBatchIngestResult>
                    {
                        IsSuccess = false,
                        StatusCode = 403,
                        Message = "Device không có quyền ingest cho battery thuộc site khác.",
                        Data = new SensorReadingBatchIngestResult
                        {
                            TotalReceived = request.Items.Count,
                            Inserted = 0,
                            Skipped = request.Items.Count
                        }
                    };
                }
            }
        }

        // Calibration profiles cho device — Redis cache TTL 5 phút (#IoT2-19/#IoT2-34).
        Dictionary<(string Channel, Guid? AssetId), IotDeviceCalibrationSnapshot>? calibrations = null;
        if (request.AuthenticatedDeviceId.HasValue)
        {
            var deviceId = request.AuthenticatedDeviceId.Value;
            var snapshot = await _calibrationCache.GetAsync(deviceId, cancellationToken);
            if (snapshot is null)
            {
                var cals = await _unitOfWork.IotDeviceCalibrations.GetAllAsync()
                    .Where(c => !c.IsDeleted && c.IotDeviceId == deviceId
                                && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow))
                    .ToListAsync(cancellationToken);
                snapshot = cals.Select(IotDeviceCalibrationSnapshot.FromEntity).ToList();
                await _calibrationCache.SetAsync(deviceId, snapshot, cancellationToken);
            }
            else
            {
                // Lọc lại expired (snapshot có thể đã quá hạn ở thời điểm cache miss → đỡ replay calibration cũ).
                var now = DateTime.UtcNow;
                snapshot = snapshot.Where(s => s.ExpiresAt == null || s.ExpiresAt > now).ToList();
            }
            calibrations = snapshot.ToDictionary(
                c => (c.Channel.ToLowerInvariant(), c.BatteryAssetId),
                c => c);
        }

        // ─── GH-763: lọc trùng TRƯỚC khi add ───
        //
        // `sensor_readings` có PK tổ hợp (time, battery_asset_id). Handler cũ add mọi item hợp lệ
        // rồi mới `SaveChangesAsync`, nên chỉ cần một số đo đã tồn tại là Postgres ném 23505 và
        // API trả 500 — kéo theo CẢ batch bị rollback, kể cả các số đo MỚI. Thiết bị gửi lại thì
        // lại 500 y hệt, nên dữ liệu mới không bao giờ vào được.
        //
        // Lấy sẵn các khoá đã có trong DB, giới hạn theo tập asset và khoảng thời gian của batch
        // (một truy vấn, không phải N). KHÔNG lọc `IsDeleted` — `SensorReading` không có cột đó,
        // và kể cả có thì một dòng đã xoá mềm vẫn chiếm PK nên vẫn phải coi là trùng.
        var batchKeys = request.Items
            .Select(i => (i.BatteryAssetId, Time: NormalizeToUtc(i.Time)))
            .ToList();

        // Vừa là ảnh chụp DB, vừa là sổ ghi các khoá đã gặp trong batch — xem vòng lặp bên dưới.
        var seenKeys = new HashSet<(Guid AssetId, DateTime Time)>();
        if (batchKeys.Count > 0)
        {
            var dedupeAssetIds = batchKeys.Select(k => k.BatteryAssetId).Distinct().ToList();
            var minTime = batchKeys.Min(k => k.Time);
            var maxTime = batchKeys.Max(k => k.Time);

            var found = await _unitOfWork.SensorReadings.GetAllAsync()
                .Where(r => dedupeAssetIds.Contains(r.BatteryAssetId)
                            && r.Time >= minTime && r.Time <= maxTime)
                .Select(r => new { r.BatteryAssetId, r.Time })
                .ToListAsync(cancellationToken);

            foreach (var row in found)
                seenKeys.Add((row.BatteryAssetId, row.Time));
        }

        var inserted = 0;
        var skipped = 0;
        var duplicates = 0;
        var rejectedOutliers = 0;
        var liveReadings = new List<LiveReadingDto>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!assets.TryGetValue(item.BatteryAssetId, out var asset))
            {
                skipped++;
                _metrics.RejectionRecorded("mapping_invalid");
                continue;
            }

            var voltage = ApplyCalibration(calibrations, "voltage", item.BatteryAssetId, item.Voltage);
            var current = ApplyCalibration(calibrations, "current", item.BatteryAssetId, item.Current);
            var temperature = ApplyCalibration(calibrations, "temperature", item.BatteryAssetId, item.Temperature);

            if (IsOutlier(voltage, current, temperature, item.SocPercent, item.SohPercent))
            {
                rejectedOutliers++;
                _metrics.RejectionRecorded("sensor_outlier");
                continue;
            }

            var readingTime = NormalizeToUtc(item.Time);

            // GH-763 — số đo đã có (hoặc lặp ngay trong batch này) thì BỎ QUA, không phải lỗi.
            // Thiết bị gửi lại sau timeout là chuyện thường; câu trả lời đúng là "đã có rồi",
            // chứ không phải 500 kéo đổ cả batch.
            // `Add` trả false khi khoá ĐÃ có — nên một phép này bắt cả hai ca: trùng với dòng
            // trong DB, và trùng với item trước đó trong CHÍNH batch này (hai item cùng khoá sẽ
            // làm EF ném ngay ở change tracker, cũng ra 500, chỉ khác thông báo).
            if (!seenKeys.Add((item.BatteryAssetId, readingTime)))
            {
                duplicates++;
                _metrics.RejectionRecorded("duplicate_reading");
                continue;
            }

            await _unitOfWork.SensorReadings.AddAsync(new SensorReadingEntity
            {
                Time = readingTime,
                BatteryAssetId = item.BatteryAssetId,
                Voltage = voltage,
                Current = current,
                Temperature = temperature,
                SocPercent = item.SocPercent,
                CycleCount = item.CycleCount,
                SohPercent = item.SohPercent,
                ChargingState = item.ChargingState,
                SourceDeviceId = item.SourceDeviceId?.Trim(),
                InternalResistanceMilliohm = item.InternalResistanceMilliohm,
                CellVoltageDeltaMv = item.CellVoltageDeltaMv,
                SourceType = item.SourceType,
                BmsErrorCode = item.BmsErrorCode?.Trim(),
                SensorSourceCode = item.SensorSourceCode?.Trim()
            });

            if (!asset.LastSensorReadingAt.HasValue || asset.LastSensorReadingAt.Value < readingTime)
                asset.LastSensorReadingAt = readingTime;

            _unitOfWork.BatteryAssets.UpdateAsync(asset);
            inserted++;

            // Sprint BE-IoT-Realtime (#616) — gom reading ĐÃ SẠCH (calibrate + loại outlier) để stream SSE sau commit.
            liveReadings.Add(new LiveReadingDto
            {
                BatteryAssetId = item.BatteryAssetId,
                CustomerId = asset.CustomerId,
                SiteId = asset.SiteId,
                BatteryTypeId = asset.BatteryTypeId,
                Time = readingTime,
                Voltage = voltage,
                Current = current,
                Temperature = temperature,
                SocPercent = item.SocPercent,
                SohPercent = item.SohPercent,
                CycleCount = item.CycleCount,
                ChargingState = (int?)item.ChargingState,
                InternalResistanceMilliohm = item.InternalResistanceMilliohm,
                CellVoltageDeltaMv = item.CellVoltageDeltaMv,
                BmsErrorCode = item.BmsErrorCode?.Trim(),
                SourceDeviceId = item.SourceDeviceId?.Trim(),
                SourceType = (int)item.SourceType,
                SensorSourceCode = item.SensorSourceCode?.Trim()
            });
        }

        // ─── Device-level housekeeping ───
        IotDeviceEntity? device = null;
        if (request.AuthenticatedDeviceId.HasValue)
        {
            device = await _unitOfWork.IotDevices.GetAllAsync()
                .FirstOrDefaultAsync(d => d.Id == request.AuthenticatedDeviceId.Value, cancellationToken);
        }

        if (device is not null)
        {
            var deviceLabel = device.Id.ToString();
            _metrics.IngestRecorded(deviceLabel, inserted);

            if (inserted > 0)
            {
                device.LastSeenAt = DateTime.UtcNow;
                if (device.Status == IotDeviceStatusEnum.Offline || device.Status == IotDeviceStatusEnum.Pending)
                    device.Status = IotDeviceStatusEnum.Active;
            }

            // ─── Sprint IoT-2 #IoT2-17 — auto-disable outlier ───
            if (rejectedOutliers > 0)
            {
                var now = DateTime.UtcNow;
                if (!device.OutlierWindowStartedAt.HasValue || (now - device.OutlierWindowStartedAt.Value) > OutlierWindow)
                {
                    device.OutlierWindowStartedAt = now;
                    device.OutlierIncidentCount = rejectedOutliers;
                }
                else
                {
                    device.OutlierIncidentCount += rejectedOutliers;
                }

                if (device.OutlierIncidentCount > OutlierThresholdPerHour && device.Status != IotDeviceStatusEnum.Decommissioned)
                {
                    device.Status = IotDeviceStatusEnum.Decommissioned;
                    device.AutoDecommissionedAt = now;
                    _metrics.DeviceAutoDecommissioned(deviceLabel);
                    _logger.LogWarning(
                        "Device {DeviceId} auto-decommissioned — {Count} outliers within window starting {WindowStart}",
                        device.Id, device.OutlierIncidentCount, device.OutlierWindowStartedAt);

                    // Alert Admin: tạo Alert level Critical liên kết bất kỳ asset gắn device (best-effort).
                    var firstAsset = assets.Values.FirstOrDefault();
                    if (firstAsset is not null)
                    {
                        await _unitOfWork.Alerts.AddAsync(new AlertEntity
                        {
                            BatteryAssetId = firstAsset.Id,
                            SiteId = firstAsset.SiteId,
                            AnomalyType = AnomalyTypeEnum.DeviceOffline,
                            Severity = AlertSeverityEnum.Critical,
                            DetectedAt = now,
                            Status = AlertStatusEnum.Open,
                            DedupWindowEndUtc = now.AddHours(6)
                        });
                    }
                }
            }

            _unitOfWork.IotDevices.UpdateAsync(device);
        }

        // ─── Persist idempotency record (#IoT2-16) ───
        if (!string.IsNullOrWhiteSpace(request.DeviceCode) && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await _unitOfWork.SensorIngestIdempotencyRecords.AddAsync(new SensorIngestIdempotencyRecord
            {
                // PK `id` cấu hình ValueGeneratedNever() → app PHẢI cấp Id (khớp convention
                // các handler khác). Thiếu dòng này → Id = Guid.Empty → insert thứ 2 trở đi
                // trùng PK "PK_sensor_ingest_idempotency_records" → 500 (chặn toàn bộ ingest
                // có Idempotency-Key, gồm cả firmware ESP32 thật).
                Id = Guid.NewGuid(),
                DeviceCode = request.DeviceCode!,
                IdempotencyKey = request.IdempotencyKey!,
                Inserted = inserted,
                Skipped = skipped + rejectedOutliers + duplicates,
                TotalReceived = request.Items.Count,
                Message = BuildMessage(rejectedOutliers, duplicates),
                ExpiresAt = DateTime.UtcNow.Add(IdempotencyTtl)
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Sprint BE-IoT-Realtime (#616) — soft-dependency: publish SAU commit, lỗi KHÔNG chặn ingest.
        if (liveReadings.Count > 0)
        {
            try
            { await _telemetryPublisher.PublishAsync(liveReadings, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Telemetry realtime publish thất bại — bỏ qua."); }

            // Sprint Bonus NS-04 (#649) — rolling min/max nạp/xả (event `stats`). Try/catch RIÊNG,
            // soft-dependency độc lập: lỗi stats không được ảnh hưởng publish reading (và ngược lại).
            try
            { await _telemetryStatsService.AccumulateAndPublishAsync(liveReadings, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Telemetry stats accumulate/publish thất bại — bỏ qua."); }
        }

        return new CommonResponse<SensorReadingBatchIngestResult>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = BuildMessage(rejectedOutliers, duplicates),
            Data = new SensorReadingBatchIngestResult
            {
                TotalReceived = request.Items.Count,
                Inserted = inserted,
                // GH-763 — số đo trùng tính vào `skipped`, KHÔNG phải lỗi: thiết bị gửi lại sau
                // timeout là chuyện thường, và firmware đọc chính hai con số này để biết backend
                // đã nhận đủ hay chưa (xem GH-748).
                Skipped = skipped + rejectedOutliers + duplicates
            }
        };
    }

    private static decimal ApplyCalibration(
        Dictionary<(string Channel, Guid? AssetId), IotDeviceCalibrationSnapshot>? cals,
        string channel,
        Guid assetId,
        decimal raw)
    {
        if (cals is null)
            return raw;
        if (cals.TryGetValue((channel, assetId), out var c) || cals.TryGetValue((channel, null), out c))
            return raw * c.Scale + c.Offset;
        return raw;
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-17 — outlier filter §52.5.
    /// Loại reading vượt cực biên phần cứng: V&gt;1000 hoặc &lt;0, |I|&gt;1000, T ngoài [-50..150],
    /// SOC ngoài [0..100], SOH ngoài [0..100] (nullable — bỏ qua khi null).
    /// </summary>
    /// <summary>
    /// GH-763 — thông báo phải TÁCH số đo trùng khỏi outlier: hai thứ này đòi hai cách xử lý
    /// khác hẳn nhau. Trùng là thiết bị gửi lại (bình thường); outlier là cảm biến đang hỏng.
    /// Gộp chung một câu thì người trực không biết có phải đi kiểm phần cứng hay không.
    /// </summary>
    private static string BuildMessage(int rejectedOutliers, int duplicates)
    {
        if (rejectedOutliers == 0 && duplicates == 0)
            return "Ghi nhận sensor readings thành công.";

        var parts = new List<string>(2);
        if (rejectedOutliers > 0) parts.Add($"{rejectedOutliers} outlier bị loại");
        if (duplicates > 0) parts.Add($"{duplicates} số đo đã có từ trước");
        return $"Ghi nhận readings — {string.Join(", ", parts)}.";
    }

    /// <summary>
    /// GH-763 — chuẩn hoá mốc thời gian về UTC. Tách riêng để việc DÒ TRÙNG và việc GHI dùng
    /// đúng một phép biến đổi: lệch nhau là dò hụt rồi vẫn dính 23505 lúc lưu.
    /// </summary>
    private static DateTime NormalizeToUtc(DateTime time)
        => time.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
            : time.ToUniversalTime();

    private static bool IsOutlier(decimal voltage, decimal current, decimal temperature, decimal socPercent, decimal? sohPercent)
    {
        if (voltage < 0 || voltage > MaxVoltage)
            return true;
        if (Math.Abs(current) > MaxCurrent)
            return true;
        if (temperature < MinTemperature || temperature > MaxTemperature)
            return true;
        if (socPercent < MinSoc || socPercent > MaxSoc)
            return true;
        if (sohPercent.HasValue && (sohPercent.Value < MinSoh || sohPercent.Value > MaxSoh))
            return true;
        return false;
    }
}
