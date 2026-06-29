using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class ChatAiSuggestion : AuditableEntity
{
    public Guid TicketId { get; set; }
    public DateTime SuggestedAt { get; set; }
    public ChatAiIntentEnum Intent { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public int? SelectedIndex { get; set; }
    public bool EditedBeforePost { get; set; }
    public Guid? FinalChatId { get; set; }

    public required Ticket Ticket { get; set; }
}
