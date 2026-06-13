using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Query.KnowledgeBase;

public class CopyKbArticleTemplateQuery : IRequest<CommonResponse<KbArticleTemplateDto>>
{
    public Guid ArticleId { get; set; }
}

public class KbArticleTemplateDto
{
    public int Category { get; set; }
    public string Symptoms { get; set; } = string.Empty;
    public string DiagnosisSteps { get; set; } = string.Empty;
    public string SolutionSteps { get; set; } = string.Empty;
    public string? RecommendedParts { get; set; }
    public List<string> Tags { get; set; } = new();
}
