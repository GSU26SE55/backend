namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbUsageStatsDTO
{
    /// <summary>
    /// ID của bài viết Knowledge Base.
    /// </summary>
    public string KbArticleId { get; set; } = string.Empty;
    public string KbArticleCode { get; set; } = string.Empty;
    public string KbArticleTitle { get; set; } = string.Empty;
    /// <summary>
    /// Total references.
    /// </summary>
    public int TotalReferences { get; set; }
    public KbUsageByTypeDTO ByType { get; set; } = new();
}

public class KbUsageByTypeDTO
{
    /// <summary>
    /// Consulted during resolve.
    /// </summary>
    public int ConsultedDuringResolve { get; set; }
    public int ProvidedToCustomer { get; set; }
    public int GeneratedAfterResolve { get; set; }
}
