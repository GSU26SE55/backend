using TicketService.Domain.Entities;

namespace TicketService.Application.Interfaces.Services;

public interface IBlogGeneratorService
{
    /// <summary>
    /// Gọi DeepSeek để sinh nội dung blog từ KB article.
    /// Trả về HTML đã render qua Markdig.
    /// </summary>
    Task<string> GenerateFromKbArticleAsync(KnowledgeBaseArticle article, CancellationToken ct = default);
}
