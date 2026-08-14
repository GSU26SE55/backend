using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

/// <summary>
/// Durable audit record for a command issued to an IoT gateway and the device
/// acknowledgement received later over MQTT.
/// </summary>
public class IotDeviceCommand : AuditableEntity
{
    public Guid IotDeviceId { get; set; }
    public IotDevice IotDevice { get; set; } = null!;

    public Guid? BatteryAssetId { get; set; }
    public BatteryAsset? BatteryAsset { get; set; }

    public string CmdId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ParamsJson { get; set; } = "{}";
    public IotDeviceCommandStatusEnum Status { get; set; } = IotDeviceCommandStatusEnum.Pending;
    public string? ResultJson { get; set; }
    public string? AckError { get; set; }
    public DateTime? AckedAt { get; set; }

    /// <summary>The account that caused the physical action; null is reserved for system commands.</summary>
    public Guid? IssuedByAccountId { get; set; }
}
