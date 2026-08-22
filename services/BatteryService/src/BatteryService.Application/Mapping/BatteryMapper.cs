using BatteryService.Application.DTOs;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;

namespace BatteryService.Application.Mapping;

public static class BatteryMapper
{
    public static BatteryTypeDto ToDto(BatteryType batteryType)
    {
        return new BatteryTypeDto
        {
            Id = batteryType.Id.ToString(),
            Name = batteryType.Name,
            Manufacturer = batteryType.Manufacturer,
            NominalCapacityAh = batteryType.NominalCapacityAh,
            NominalVoltage = batteryType.NominalVoltage,
            Chemistry = batteryType.Chemistry,
            MaxCycleCount = batteryType.MaxCycleCount,
            Description = batteryType.Description,
            CreatedAt = batteryType.CreatedAt
        };
    }

    public static BatteryAssetDto ToDto(BatteryAsset asset, string customerName = "")
    {
        return new BatteryAssetDto
        {
            Id = asset.Id.ToString(),
            SerialNumber = asset.SerialNumber,
            BatteryTypeId = asset.BatteryTypeId.ToString(),
            BatteryTypeName = asset.BatteryType?.Name ?? string.Empty,
            SiteId = asset.SiteId?.ToString(),
            SiteName = asset.Site?.Name,
            CustomerId = asset.CustomerId.ToString(),
            CustomerName = customerName,
            InstallDate = asset.InstallDate,
            WarrantyEndDate = asset.WarrantyEndDate,
            WarrantyStatus = asset.WarrantyStatus,
            Location = asset.Location,
            Latitude = asset.Latitude,
            Longitude = asset.Longitude,
            Status = asset.Status,
            Notes = asset.Notes,
            LastSensorReadingAt = asset.LastSensorReadingAt,
            CreatedAt = asset.CreatedAt
        };
    }

    public static SiteDto ToDto(Site site, string customerName = "")
    {
        return new SiteDto
        {
            Id = site.Id.ToString(),
            Name = site.Name,
            CustomerId = site.CustomerId.ToString(),
            CustomerName = customerName,
            Address = site.Address,
            Latitude = site.Latitude,
            Longitude = site.Longitude,
            InstallDate = site.InstallDate,
            Status = site.Status,
            ContactPersonName = site.ContactPersonName,
            ContactPersonPhone = site.ContactPersonPhone,
            BatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted),
            ActiveBatteryAssetCount = site.BatteryAssets.Count(asset => !asset.IsDeleted && asset.Status == BatteryStatusEnum.Active),
            CreatedAt = site.CreatedAt
        };
    }

    public static ThresholdConfigDto ToDto(ThresholdConfig config)
    {
        return new ThresholdConfigDto
        {
            Id = config.Id.ToString(),
            BatteryTypeId = config.BatteryTypeId.ToString(),
            BatteryTypeName = config.BatteryType?.Name ?? string.Empty,
            VoltageMin = config.VoltageMin,
            VoltageMax = config.VoltageMax,
            TemperatureMax = config.TemperatureMax,
            TemperatureMin = config.TemperatureMin,
            SocWarningThreshold = config.SocWarningThreshold,
            SocCriticalThreshold = config.SocCriticalThreshold,
            CurrentMaxCharge = config.CurrentMaxCharge,
            CurrentMaxDischarge = config.CurrentMaxDischarge,
            SohWarningThreshold = config.SohWarningThreshold,
            SohCriticalThreshold = config.SohCriticalThreshold,
            EffectiveFromUtc = config.EffectiveFromUtc,
            IsActive = config.IsActive
        };
    }

    public static SensorReadingDto ToDto(SensorReading reading)
    {
        return new SensorReadingDto
        {
            Time = reading.Time,
            BatteryAssetId = reading.BatteryAssetId.ToString(),
            Voltage = reading.Voltage,
            Current = reading.Current,
            Temperature = reading.Temperature,
            SocPercent = reading.SocPercent,
            CycleCount = reading.CycleCount,
            SourceDeviceId = reading.SourceDeviceId
        };
    }

    /// <param name="customerName">
    /// Tên khách sở hữu alert. Truyền vào thay vì tra trong mapper vì Account nằm ở repository
    /// khác — cùng quy ước với ToDto của BatteryAsset/Site ở trên.
    /// </param>
    public static AlertDto ToDto(Alert alert, string customerName = "")
    {
        return new AlertDto
        {
            CustomerName = customerName,
            Id = alert.Id.ToString(),
            BatteryAssetId = alert.BatteryAssetId.ToString(), // Guid? null → "" (alert cấp site)
            IotDeviceId = alert.IotDeviceId?.ToString(),
            SiteId = alert.SiteId?.ToString(),                // Sprint Bonus NS-21 (#661)
            BatterySerialNumber = alert.BatteryAsset?.SerialNumber ?? string.Empty,
            AnomalyType = alert.AnomalyType,
            Severity = alert.Severity,
            ThresholdValue = alert.ThresholdValue,
            ActualValue = alert.ActualValue,
            Unit = alert.Unit,
            DetectedAt = alert.DetectedAt,
            Status = alert.Status,
            TicketId = alert.TicketId?.ToString(),
            AcknowledgedByUserId = alert.AcknowledgedByUserId?.ToString(),
            AcknowledgedAt = alert.AcknowledgedAt,
            ResolvedAt = alert.ResolvedAt,
            DedupWindowEndUtc = alert.DedupWindowEndUtc,
            AiPrescriptionId = alert.AiPrescriptionId,
            CreatedAt = alert.CreatedAt
        };
    }
}
