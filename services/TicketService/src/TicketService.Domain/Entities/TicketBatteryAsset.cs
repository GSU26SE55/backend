using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class TicketBatteryAsset : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid BatteryAssetId { get; set; }

    public Ticket? Ticket { get; set; }
}
