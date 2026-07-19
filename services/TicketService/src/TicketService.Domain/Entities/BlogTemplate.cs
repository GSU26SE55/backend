using System.Text.Json;
using SharedKernels.Domain;

namespace TicketService.Domain.Entities;

public class BlogTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JsonDocument ContentHtml { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }

    // Navigation
    public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
}
