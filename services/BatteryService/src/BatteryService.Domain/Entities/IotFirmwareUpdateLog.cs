using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Sprint IoT-1 (#242, #245) — log mỗi attempt OTA của 1 <see cref="IotDevice"/> với 1 <see cref="IotFirmwareRelease"/>.
/// Device PUT progress qua <c>/api/iot-devices/firmware-update-log/{id}</c>.
/// </summary>
public class IotFirmwareUpdateLog : AuditableEntity
{
    public Guid IotDeviceId { get; set; }

    public IotDevice IotDevice { get; set; } = null!;

    public Guid IotFirmwareReleaseId { get; set; }

    public IotFirmwareRelease IotFirmwareRelease { get; set; } = null!;

    /// <summary>Version device đang chạy trước khi flash. Snapshot cho rollback debug.</summary>
    public string? FromVersion { get; set; }

    /// <summary>Version target.</summary>
    public string ToVersion { get; set; } = string.Empty;

    public IotFirmwareUpdateStatusEnum Status { get; set; } = IotFirmwareUpdateStatusEnum.Pending;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Error message khi Status = Failed.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Số byte device đã download (cho progress UI).</summary>
    public long? BytesDownloaded { get; set; }
}
