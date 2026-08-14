using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Blog;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Consumer cho <see cref="BlogGenerationRequestedEvent"/>:
/// 1. Load KB article → gọi DeepSeek → Markdown → HTML (qua IBlogGeneratorService)
/// 2. Transaction: update BlogPost (Draft, v1) + tạo BlogPostVersion(v1)
/// 3. Publish <see cref="BlogGenerationStatusChangedEvent"/> để NotificationService gửi InApp notification
/// Throw exception → MassTransit retry (3 lần mặc định).
/// </summary>
public class BlogGenerationConsumer : IConsumer<BlogGenerationRequestedEvent>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IBlogGeneratorService _blogGenerator;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;
    private readonly ILogger<BlogGenerationConsumer> _logger;

    public BlogGenerationConsumer(
        ITicketUnitOfWork uow,
        IBlogGeneratorService blogGenerator,
        IIntegrationEventOutboxWriter outboxWriter,
        ILogger<BlogGenerationConsumer> logger)
    {
        _uow = uow;
        _blogGenerator = blogGenerator;
        _outboxWriter = outboxWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BlogGenerationRequestedEvent> context)
    {
        var evt = context.Message;
        var ct = context.CancellationToken;

        var post = await _uow.BlogPosts.GetAllAsync()
            .Where(x => x.Id == evt.BlogPostId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (post == null)
        {
            _logger.LogWarning("BlogGenerationConsumer: BlogPost {BlogPostId} not found, skipping.", evt.BlogPostId);
            return;
        }

        if (post.Status != BlogPostStatusEnum.Generating)
        {
            _logger.LogInformation("BlogGenerationConsumer: BlogPost {BlogPostId} status={Status}, skipping.", evt.BlogPostId, post.Status);
            return;
        }

        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(x => x.Id == evt.KbArticleId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (article == null)
        {
            await MarkFailedAsync(post, "KB article not found.", evt.RequestedByUserId, ct);
            return;
        }

        string contentHtml;
        try
        {
            contentHtml = await _blogGenerator.GenerateFromKbArticleAsync(article, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BlogGenerationConsumer: DeepSeek failed for BlogPostId={BlogPostId}", evt.BlogPostId);
            await MarkFailedAsync(post, ex.Message, evt.RequestedByUserId, ct);
            throw; // rethrow → MassTransit retry (3 attempts); idempotency guard at top skips re-marking
        }

        var summary = ExtractSummary(contentHtml);
        var contentDoc = ToJsonDoc(contentHtml);

        var version = new BlogPostVersion
        {
            Id = Guid.NewGuid(),
            BlogPostId = post.Id,
            VersionNumber = 1,
            Title = post.Title,
            Summary = summary,
            ContentHtml = contentDoc,
            ChangedByUserId = evt.RequestedByUserId,
            ChangeNote = "AI auto-generated from KB article",
        };

        post.ContentHtml = contentDoc;
        post.Summary = summary;
        post.Status = BlogPostStatusEnum.Draft;
        post.CurrentVersion = 1;

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.BlogPostVersions.AddAsync(version);
            _uow.BlogPosts.UpdateAsync(post);
            await _outboxWriter.WriteAsync(new BlogGenerationStatusChangedEvent(
                post.Id, evt.RequestedByUserId, "Draft", post.Title, null));
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            _logger.LogError(ex, "BlogGenerationConsumer: DB write failed for BlogPostId={BlogPostId}", evt.BlogPostId);
            throw;
        }

        _logger.LogInformation("BlogGenerationConsumer: BlogPost {BlogPostId} generated successfully (v1).", post.Id);
    }

    private async Task MarkFailedAsync(BlogPost post, string error, Guid requestedBy, CancellationToken ct)
    {
        post.Status = BlogPostStatusEnum.GenerationFailed;

        await _uow.BeginTransactionAsync();
        try
        {
            _uow.BlogPosts.UpdateAsync(post);
            await _outboxWriter.WriteAsync(new BlogGenerationStatusChangedEvent(
                post.Id, requestedBy, "GenerationFailed", post.Title, error));
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    private static string ExtractSummary(string html)
    {
        // Strip HTML tags, decode entities, collapse whitespace — tối đa 300 ký tự
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        return text.Length <= 300 ? text : text[..297] + "...";
    }

    private static JsonDocument ToJsonDoc(string? v) =>
        string.IsNullOrWhiteSpace(v) ? JsonDocument.Parse("{}") :
        (v.TrimStart().StartsWith('{') || v.TrimStart().StartsWith('[')
            ? TryParse(v) : JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(v)));

    private static JsonDocument TryParse(string v)
    {
        try
        { return JsonDocument.Parse(v); }
        catch { return JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(v)); }
    }
}
