using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class TicketChatTranslationUser : AuditableEntity
{
    public Guid TranslationId { get; set; }
    public Guid UserId { get; set; }

    public TicketChatTranslation Translation { get; set; } = null!;
}
