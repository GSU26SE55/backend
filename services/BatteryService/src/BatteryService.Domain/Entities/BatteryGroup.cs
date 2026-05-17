using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class BatteryGroup : AuditableEntity
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = null!;

    public Guid BatteryTypeId { get; set; }

    public int BatteryCount { get; set; }

    public Site Site { get; set; } = null!;

    public BatteryType BatteryType { get; set; } = null!;

    public ICollection<BatteryAsset> BatteryAssets { get; set; } = new List<BatteryAsset>();
}
