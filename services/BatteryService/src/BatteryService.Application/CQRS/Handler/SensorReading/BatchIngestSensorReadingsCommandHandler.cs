using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.Common;
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

        var inserted = 0;
        var skipped = 0;
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

            var readingTime = item.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(item.Time, DateTimeKind.Utc)
                : item.Time.ToUniversalTime();
            // Legacy/current clients do not send SensorSourceCode. Some deployed
            // Timescale schemas include this column in the composite primary key,
            // so persist the canonical primary-source tag instead of null.
            var sensorSourceCode = string.IsNullOrWhiteSpace(item.SensorSourceCode)
                ? SensorSource.Primary
                : item.SensorSourceCode.Trim();

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
                SensorSourceCode = sensorSourceCode
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
                SensorSourceCode = sensorSourceCode
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
                Skipped = skipped + rejectedOutliers,
                TotalReceived = request.Items.Count,
                Message = rejectedOutliers > 0
                    ? $"Ghi nhận readings — {rejectedOutliers} outlier bị loại."
                    : "Ghi nhận sensor readings thành công.",
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
            Message = rejectedOutliers > 0
                ? $"Ghi nhận readings — {rejectedOutliers} outlier bị loại."
                : "Ghi nhận sensor readings thành công.",
            Data = new SensorReadingBatchIngestResult
            {
                TotalReceived = request.Items.Count,
                Inserted = inserted,
                Skipped = skipped + rejectedOutliers
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
