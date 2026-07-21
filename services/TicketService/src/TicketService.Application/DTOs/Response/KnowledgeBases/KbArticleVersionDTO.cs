using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleVersionDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string ArticleId { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    /// <summary>
    /// Minor version.
    /// </summary>
    public int MinorVersion { get; set; }
    public KbVersionStatusEnum Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public string ChangeDescription { get; set; } = string.Empty;
    /// <summary>
    /// Changed by.
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
