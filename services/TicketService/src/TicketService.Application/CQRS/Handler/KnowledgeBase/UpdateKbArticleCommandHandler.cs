using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class UpdateKbArticleCommandHandler : IRequestHandler<UpdateKbArticleCommand, CommonResponse<KbArticleDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public UpdateKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDTO>> Handle(UpdateKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
        {
            if (!command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return Fail(403, "Only Admin can update templates.");

            return await HandleTemplateUpdate(article, command, ct);
        }

        // Check authorization for regular articles
        var isCreator = article.CreatedByUserId == command.CurrentUserId;
        var isManagerOrAdmin = command.CurrentUserRole.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                               command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        if (!isCreator && !isManagerOrAdmin && !command.CurrentUserRole.Equals("Staff", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(403, "You do not have permission to update this article.");
        }

        // Manager/Admin hoặc chủ sở hữu bài viết cập nhật trực tiếp, không qua phê duyệt lại.
        if (isCreator || isManagerOrAdmin)
            return await HandleDirectUpdate(article, command, ct);

        // Determine next version numbers
        var nextMajor = article.Version + 1;
        var lastMinor = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => v.ArticleId == article.Id && v.MajorVersion == nextMajor)
            .OrderByDescending(v => v.MinorVersion)
            .Select(v => v.MinorVersion)
            .FirstOrDefaultAsync(ct);

        var nextMinor = lastMinor + 1;

        // Create new version as "Draft/Pending"
        var newVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = nextMajor,
            MinorVersion = nextMinor,
            Status = KbVersionStatusEnum.Pending,
            Title = command.Title,
            Content = J(command.Content),
            Tags = command.Tags ?? new List<string>(),
            ChangeDescription = command.ChangeDescription ?? "Staff updated content",
            ChangedBy = command.CurrentUserId
        };
        await _uow.KbArticleVersions.AddAsync(newVersion);

        article.ReviewRequired = true;
        article.Status = KbArticleStatusEnum.PendingReview;
        article.PendingReviewBy = command.CurrentUserId;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Change draft has been saved and is pending approval.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    /// <summary>
    /// Cập nhật trực tiếp cho Manager/Admin hoặc chủ sở hữu bài viết: ghi thẳng nội dung mới vào
    /// article, đồng thời lưu 1 bản ghi version đã Approved để giữ lịch sử thay đổi.
    /// </summary>
    private async Task<CommonResponse<KbArticleDTO>> HandleDirectUpdate(
        KnowledgeBaseArticle article, UpdateKbArticleCommand command, CancellationToken ct)
    {
        var nextMajor = article.Version + 1;

        await _uow.BeginTransactionAsync();
        try
        {
            var newVersion = new KbArticleVersion
            {
                Id = Guid.NewGuid(),
                ArticleId = article.Id,
                MajorVersion = nextMajor,
                MinorVersion = 0,
                Status = KbVersionStatusEnum.Approved,
                ChangeDescription = command.ChangeDescription ?? "Direct update",
                ChangedBy = command.CurrentUserId
            };
            ApplyContentToVersion(newVersion, command);

            // Ô (nextMajor, 0) thường đã bị chiếm sẵn bởi bản Pending tạo lúc khởi tạo bài viết —
            // xem KbArticleVersionSlot. AddAsync thẳng vào đây là 23505 → 500.
            await KbArticleVersionSlot.UpsertAsync(_uow, newVersion, ct);
            await KbArticleVersionSlot.RejectOtherPendingAsync(
                _uow, article.Id, nextMajor, 0, "A direct update has replaced it.", ct);

            ApplyContentToArticle(article, command);
            article.Category = command.Category;
            article.Version = nextMajor;
            article.ReviewRequired = false;
            article.PendingReviewBy = null;
            article.ManagerRejectReason = null;

            _uow.KnowledgeBaseArticles.UpdateAsync(article);
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Article updated successfully.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private async Task<CommonResponse<KbArticleDTO>> HandleTemplateUpdate(
        KnowledgeBaseArticle article, UpdateKbArticleCommand command, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync();
        try
        {
            ApplyContentToArticle(article, command);

            if (article.Status == KbArticleStatusEnum.Draft)
            {
                var pending = new KbArticleVersion
                {
                    Id = Guid.NewGuid(),
                    ArticleId = article.Id,
                    MajorVersion = article.Version + 1,
                    MinorVersion = 0,
                    Status = KbVersionStatusEnum.Pending,
                    ChangeDescription = command.ChangeDescription ?? "Admin updated template",
                    ChangedBy = command.CurrentUserId
                };
                ApplyContentToVersion(pending, command);

                await KbArticleVersionSlot.UpsertAsync(_uow, pending, ct);
            }
            else if (article.Status == KbArticleStatusEnum.Published)
            {
                // Bump major version, create Approved version record immediately
                var nextMajor = article.Version + 1;
                article.Version = nextMajor;

                var newVersion = new KbArticleVersion
                {
                    Id = Guid.NewGuid(),
                    ArticleId = article.Id,
                    MajorVersion = nextMajor,
                    MinorVersion = 0,
                    Status = KbVersionStatusEnum.Approved,
                    ChangeDescription = command.ChangeDescription ?? "Admin updated template",
                    ChangedBy = command.CurrentUserId
                };
                ApplyContentToVersion(newVersion, command);

                await KbArticleVersionSlot.UpsertAsync(_uow, newVersion, ct);
            }

            _uow.KnowledgeBaseArticles.UpdateAsync(article);
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Template updated successfully.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static void ApplyContentToArticle(KnowledgeBaseArticle article, UpdateKbArticleCommand command)
    {
        article.Title = command.Title;
        article.Content = J(command.Content);
        article.Tags = command.Tags ?? new List<string>();
    }

    private static void ApplyContentToVersion(KbArticleVersion version, UpdateKbArticleCommand command)
    {
        version.Title = command.Title;
        version.Content = J(command.Content);
        version.Tags = command.Tags ?? new List<string>();
    }

    private static JsonDocument J(string? v) => KnowledgeBaseMapper.ToJsonDoc(v);

    private static CommonResponse<KbArticleDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
