using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi DeepSeek để sinh nội dung blog từ KB article, rồi render Markdown → HTML qua Markdig.
/// Reuse <see cref="DeepSeekChatAiClient"/> (HttpClient + timeout đã cấu hình).
/// </summary>
public class DeepSeekBlogGeneratorService : IBlogGeneratorService
{
    private readonly DeepSeekChatAiClient _deepSeek;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly ChatOptions _opts;

    public DeepSeekBlogGeneratorService(
        DeepSeekChatAiClient deepSeek,
        IMarkdownRenderer markdownRenderer,
        IOptions<ChatOptions> opts)
    {
        _deepSeek = deepSeek;
        _markdownRenderer = markdownRenderer;
        _opts = opts.Value;
    }

    public async Task<string> GenerateFromKbArticleAsync(KnowledgeBaseArticle article, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(article);
        var markdown = await _deepSeek.CallAsync(_opts.DeepSeek.ApiKey, prompt, temperature: 0.6, ct);

        // Render Markdown → HTML (no attachment images allowed for AI-generated content)
        return _markdownRenderer.RenderToHtml(markdown, Array.Empty<Guid>());
    }

    private static string BuildPrompt(KnowledgeBaseArticle article)
    {
        var content = KnowledgeBaseMapper.J(article.Content);
        var tags = string.Join(", ", article.Tags);

        return $"""
                Bạn là kỹ sư kỹ thuật chuyên về bảo trì pin lithium-ion năng lượng mặt trời.
                Hãy viết một bài blog kỹ thuật bằng tiếng Việt dựa trên tài liệu kiến thức dưới đây.
                Yêu cầu:
                - Ngôn ngữ: tiếng Việt, chuyên nghiệp, dễ hiểu cho kỹ thuật viên
                - Định dạng: Markdown (dùng ## cho heading, ** cho in đậm, danh sách -/1.)
                - Cấu trúc: Giới thiệu → Triệu chứng → Nguyên nhân & chẩn đoán → Giải pháp → Lưu ý
                - Độ dài: 400–800 từ
                - Không thêm thông tin ngoài tài liệu cung cấp

                ## Tài liệu Knowledge Base

                **Tiêu đề:** {article.Title}
                **Danh mục:** {article.Category}
                **Tags:** {tags}

                **Nội dung:**
                {content}

                Hãy bắt đầu bài blog ngay (không cần lời giới thiệu về nhiệm vụ).
                """;
    }

}
