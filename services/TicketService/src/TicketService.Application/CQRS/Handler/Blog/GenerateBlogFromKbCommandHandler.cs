using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Blog;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GenerateBlogFromKbCommandHandler : IRequestHandler<GenerateBlogFromKbCommand, CommonResponse<BlogPostActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public GenerateBlogFromKbCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<CommonResponse<BlogPostActionDTO>> Handle(GenerateBlogFromKbCommand request, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(x => x.Id == request.KbArticleId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (article == null)
            return Fail(404, "KB article not found.");

        if (article.Status != KbArticleStatusEnum.Published)
            return Fail(409, "Blog can only be generated from a Published KB article.");

        // Duplicate check: KB article đã có blog Draft/Published/Generating
        var duplicateExists = await _uow.BlogPosts.AnyAsync(x =>
            x.SourceKbArticleId == request.KbArticleId
            && !x.IsDeleted
            && x.Status != BlogPostStatusEnum.Archived
            && x.Status != BlogPostStatusEnum.GenerationFailed);

        if (duplicateExists)
            return Fail(409, "This KB article already has a blog being generated or already existing. Please archive the old blog first.");

        var slug = GenerateSlug(article.Title);

        // Đảm bảo slug không trùng
        var slugConflict = await _uow.BlogPosts.AnyAsync(x => x.Slug == slug && !x.IsDeleted);
        if (slugConflict)
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

        var post = new BlogPost
        {
            Id = Guid.NewGuid(),
            Title = article.Title,
            Slug = slug,
            Summary = string.Empty,
            ContentHtml = KnowledgeBaseMapper.ToJsonDoc(null),
            Status = BlogPostStatusEnum.Generating,
            Origin = BlogPostOriginEnum.AiGeneratedFromKb,
            SourceKbArticleId = article.Id,
            AuthorUserId = request.CurrentUserId,
            CurrentVersion = 0,
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.BlogPosts.AddAsync(post);
            await _outboxWriter.WriteAsync(new BlogGenerationRequestedEvent(
                post.Id,
                article.Id,
                request.CurrentUserId));
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            return Fail(500, ex.Message);
        }

        return new CommonResponse<BlogPostActionDTO>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "AI blog generation request sent. The post will be ready in a few seconds.",
            Data = new BlogPostActionDTO
            {
                Id = post.Id.ToString(),
                Title = post.Title,
                Status = post.Status,
                CurrentVersion = post.CurrentVersion,
            }
        };
    }

    private static string GenerateSlug(string title)
    {
        // NFD decompose to separate base letters from combining diacritics, then strip diacritics
        var normalized = title.Normalize(System.Text.NormalizationForm.FormD);
        var stripped = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                stripped.Append(c);
        }

        var slug = stripped.ToString()
            .ToLowerInvariant()
            .Replace("đ", "d")
            .Replace(" ", "-");

        // Keep only ASCII alphanumeric and hyphens, collapse consecutive hyphens
        var result = new System.Text.StringBuilder(slug.Length);
        foreach (var c in slug)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
                result.Append(c);
        }

        var final = System.Text.RegularExpressions.Regex.Replace(result.ToString(), "-+", "-").Trim('-');
        return string.IsNullOrEmpty(final) ? $"blog-{Guid.NewGuid():N}"[..14] : final;
    }

    private static CommonResponse<BlogPostActionDTO> Fail(int statusCode, string message) =>
        new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
