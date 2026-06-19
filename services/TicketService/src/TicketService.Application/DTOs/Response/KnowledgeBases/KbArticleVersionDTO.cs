namespace TicketService.Application.DTOs.Response.KnowledgeBases;

public class KbArticleVersionDTO
{
    public string Id { get; set; } = string.Empty;
    public string ArticleId { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public int MinorVersion { get; set; }
    public int Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public List<string>? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
    public string ChangeDescription { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
