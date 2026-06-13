using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SensorReadingEntity = BatteryService.Domain.Entities.SensorReading;

namespace BatteryService.Application.CQRS.Handler.SensorReading;

/// <summary>
/// Sprint IoT-1 (#246, #247):
/// - Resolve BatteryAssetId từ serial nếu cần.
/// - Reject outlier sensor (hard-coded bounds — §1050 overall.md).
/// - Apply IotDeviceCalibration <c>raw * Scale + Offset</c> trước khi lưu.
/// - Update IotDevice.LastSeenAt.
/// </summary>
public class BatchIngestSensorReadingsCommandHandler : IRequestHandler<BatchIngestSensorReadingsCommand, CommonResponse<SensorReadingBatchIngestResult>>
{
    // Outlier bounds — §1050 overall.md hardware noise filter.
    private const decimal MaxVoltage = 100m;
    private const decimal MinTemperature = -50m;
    private const decimal MaxTemperature = 120m;
    private const decimal MaxCurrent = 1000m;

    private readonly IBatteryUnitOfWork _unitOfWork;

    public BatchIngestSensorReadingsCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SensorReadingBatchIngestResult>> Handle(BatchIngestSensorReadingsCommand request, CancellationToken cancellationToken)
    {
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

        // Calibration profiles cho device + những battery asset trong batch.
        Dictionary<(string Channel, Guid? AssetId), IotDeviceCalibration>? calibrations = null;
        if (request.AuthenticatedDeviceId.HasValue)
        {
            var cals = await _unitOfWork.IotDeviceCalibrations.GetAllAsync()
                .Where(c => !c.IsDeleted && c.IotDeviceId == request.AuthenticatedDeviceId.Value
                            && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow))
                .ToListAsync(cancellationToken);
            calibrations = cals.ToDictionary(c => (c.Channel.ToLowerInvariant(), (Guid?)c.BatteryAssetId), c => c);
        }

        var inserted = 0;
        var skipped = 0;
        var rejectedOutliers = 0;
        foreach (var item in request.Items)
        {
            if (!assets.TryGetValue(item.BatteryAssetId, out var asset))
            {
                skipped++;
                continue;
            }

            var voltage = ApplyCalibration(calibrations, "voltage", item.BatteryAssetId, item.Voltage);
            var current = ApplyCalibration(calibrations, "current", item.BatteryAssetId, item.Current);
            var temperature = ApplyCalibration(calibrations, "temperature", item.BatteryAssetId, item.Temperature);

            if (IsOutlier(voltage, current, temperature))
            {
                rejectedOutliers++;
                continue;
            }

            var readingTime = item.Time.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(item.Time, DateTimeKind.Utc)
                : item.Time.ToUniversalTime();

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
        }

        // Update LastSeenAt cho device đang push.
        if (request.AuthenticatedDeviceId.HasValue && inserted > 0)
        {
            var device = await _unitOfWork.IotDevices.GetAllAsync()
                .FirstOrDefaultAsync(d => d.Id == request.AuthenticatedDeviceId.Value, cancellationToken);
            if (device is not null)
            {
                device.LastSeenAt = DateTime.UtcNow;
                if (device.Status == IotDeviceStatusEnum.Offline || device.Status == IotDeviceStatusEnum.Pending)
                    device.Status = IotDeviceStatusEnum.Active;
                _unitOfWork.IotDevices.UpdateAsync(device);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        Dictionary<(string Channel, Guid? AssetId), IotDeviceCalibration>? cals,
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

    private static bool IsOutlier(decimal voltage, decimal current, decimal temperature)
    {
        if (voltage < 0 || voltage > MaxVoltage)
            return true;
        if (Math.Abs(current) > MaxCurrent)
            return true;
        if (temperature < MinTemperature || temperature > MaxTemperature)
            return true;
        return false;
    }
}
