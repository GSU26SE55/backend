using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class TicketChat : AuditableEntity
{
    public Guid TicketId { get; set; }
    public Guid AuthorUserId { get; set; }
    public ActorRoleEnum AuthorRole { get; set; }
    public string? AuthorDisplayName { get; set; }
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public List<Guid> AttachmentFileIds { get; set; } = new();
    public DateTime? EditedAt { get; set; }
    public int EditCount { get; set; } = 0;
    public Guid? LastEditedByUserId { get; set; }
    public ChatBodyFormatEnum BodyFormat { get; set; } = ChatBodyFormatEnum.PlainText;
    public string? BodyHtml { get; set; }
    public Guid? ParentChatId { get; set; }
    public Guid? ThreadRootId { get; set; }
    public int ReplyCount { get; set; } = 0;
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }
    public Guid? PinnedByUserId { get; set; }
    public string? OriginalLanguage { get; set; }

    // GDPR — #569
    public bool IsRedacted { get; set; } = false;
    public DateTime? RedactedAt { get; set; }

    public required Ticket Ticket { get; set; }
    public TicketChat? ParentChat { get; set; }
}
