using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class TicketChatHide : AuditableEntity
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }

    public TicketChat Chat { get; set; } = null!;
}
