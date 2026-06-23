using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class ChatTemplate : AuditableEntity
{
    public required string Name { get; set; }
    public required string Content { get; set; }
    public ChatTemplateCategoryEnum Category { get; set; }
    public bool IsInternalDefault { get; set; }
    public Guid CreatedByUserId { get; set; }
    public ChatTemplateScopeEnum Scope { get; set; }
    public Guid? TeamId { get; set; }
    public int UsageCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
}
