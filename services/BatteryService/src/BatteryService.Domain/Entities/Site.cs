using BatteryService.Domain.Enums;
using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class Site : AuditableEntity
{
    public string Name { get; set; } = null!;

    public Guid CustomerId { get; set; }

    public string? Address { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public DateTime InstallDate { get; set; }

    public SiteStatusEnum Status { get; set; } = SiteStatusEnum.Active;

    public string? ContactPersonName { get; set; }

    public string? ContactPersonPhone { get; set; }

    public ICollection<BatteryAsset> BatteryAssets { get; set; } = new List<BatteryAsset>();
}
