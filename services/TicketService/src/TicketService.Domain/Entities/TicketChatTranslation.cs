using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketChatTranslation : AuditableEntity
{
    public Guid ChatId { get; set; }
    public required string TargetLanguage { get; set; }
    public required string TranslatedBody { get; set; }
    public TranslationProviderEnum Provider { get; set; }
    public DateTime TranslatedAt { get; set; }

    public required TicketChat Chat { get; set; }
    public ICollection<TicketChatTranslationUser> Users { get; set; } = new List<TicketChatTranslationUser>();
}
