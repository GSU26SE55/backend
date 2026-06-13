using BatteryService.Application.DTOs;
using BatteryService.Domain.Entities;

namespace BatteryService.Application.Mapping;

public static class IotDeviceMapper
{
    public static IotDeviceDto ToDto(IotDevice e, string? siteName = null, string? targetFirmwareVersion = null) => new()
    {
        Id = e.Id.ToString(),
        DeviceCode = e.DeviceCode,
        DisplayName = e.DisplayName,
        SiteId = e.SiteId.ToString(),
        SiteName = siteName,
        HardwareRevision = e.HardwareRevision,
        Status = e.Status,
        CurrentFirmwareVersion = e.CurrentFirmwareVersion,
        TargetFirmwareReleaseId = e.TargetFirmwareReleaseId?.ToString(),
        TargetFirmwareVersion = targetFirmwareVersion,
        ApiKeyScopes = e.ApiKeyScopes,
        ApiKeyLastFour = e.ApiKeyLastFour,
        ApiKeyIssuedAt = e.ApiKeyIssuedAt,
        ApiKeyRevokedAt = e.ApiKeyRevokedAt,
        LastSeenAt = e.LastSeenAt,
        LastProvisionedAt = e.LastProvisionedAt,
        LastOfflineAt = e.LastOfflineAt,
        HeartbeatIntervalSeconds = e.HeartbeatIntervalSeconds,
        LastClockSkewSeconds = e.LastClockSkewSeconds,
        Notes = e.Notes,
        CreatedAt = e.CreatedAt
    };

    public static IotDeviceCreatedDto ToCreatedDto(IotDevice e, string rawApiKey, string? siteName = null)
    {
        var dto = ToDto(e, siteName);
        return new IotDeviceCreatedDto
        {
            Id = dto.Id,
            DeviceCode = dto.DeviceCode,
            DisplayName = dto.DisplayName,
            SiteId = dto.SiteId,
            SiteName = dto.SiteName,
            HardwareRevision = dto.HardwareRevision,
            Status = dto.Status,
            CurrentFirmwareVersion = dto.CurrentFirmwareVersion,
            TargetFirmwareReleaseId = dto.TargetFirmwareReleaseId,
            TargetFirmwareVersion = dto.TargetFirmwareVersion,
            ApiKeyScopes = dto.ApiKeyScopes,
            ApiKeyLastFour = dto.ApiKeyLastFour,
            ApiKeyIssuedAt = dto.ApiKeyIssuedAt,
            ApiKeyRevokedAt = dto.ApiKeyRevokedAt,
            LastSeenAt = dto.LastSeenAt,
            LastProvisionedAt = dto.LastProvisionedAt,
            LastOfflineAt = dto.LastOfflineAt,
            HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
            LastClockSkewSeconds = dto.LastClockSkewSeconds,
            Notes = dto.Notes,
            CreatedAt = dto.CreatedAt,
            RawApiKey = rawApiKey
        };
    }

    public static IotFirmwareReleaseDto ToDto(IotFirmwareRelease e) => new()
    {
        Id = e.Id.ToString(),
        Version = e.Version,
        HardwareRevision = e.HardwareRevision,
        ArtifactUrl = e.ArtifactUrl,
        Sha256Checksum = e.Sha256Checksum,
        ArtifactSizeBytes = e.ArtifactSizeBytes,
        ReleaseNotes = e.ReleaseNotes,
        IsPublished = e.IsPublished,
        PublishedAt = e.PublishedAt,
        IsArchived = e.IsArchived,
        CreatedAt = e.CreatedAt
    };
}
