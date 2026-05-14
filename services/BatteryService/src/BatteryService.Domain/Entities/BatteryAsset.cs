using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class BatteryAsset : AuditableEntity
{
    public string SerialNumber { get; set; } = null!;

    public Guid BatteryTypeId { get; set; }

    public Guid? SiteId { get; set; }

    public Guid? BatteryGroupId { get; set; }

    public Guid CustomerId { get; set; }

    public DateTime InstallDate { get; set; }

    public DateTime? WarrantyEndDate { get; set; }

    public WarrantyStatusEnum WarrantyStatus { get; set; } = WarrantyStatusEnum.Active;

    public string? Location { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public BatteryStatusEnum Status { get; set; } = BatteryStatusEnum.Active;

    public string? Notes { get; set; }

    public DateTime? LastSensorReadingAt { get; set; }

    public BatteryType BatteryType { get; set; } = null!;

    public Site? Site { get; set; }

    public BatteryGroup? BatteryGroup { get; set; }

    public ICollection<SensorReading> SensorReadings { get; set; } = new List<SensorReading>();

    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
