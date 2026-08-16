using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gọi DeepSeek để sinh nội dung blog từ guide article, rồi render Markdown → HTML qua Markdig.
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
                You are a field engineer specialising in the maintenance of lithium-ion
                solar battery storage. Write a technical blog post in English based on the
                guide article below.

                Requirements:
                - Language: English, professional, readable by a field technician
                - Format: Markdown (## for headings, ** for bold, -/1. for lists)
                - Structure: Introduction → Symptoms → Causes & diagnosis → Resolution → Notes
                - Length: 400-800 words
                - Do not add information that is not in the source article

                ## Source guide article

                **Title:** {article.Title}
                **Category:** {article.Category}
                **Tags:** {tags}

                **Content:**
                {content}

                Start the blog post directly, with no preamble about the task.
                """;
    }

}
