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

public class RollbackKbArticleCommandHandler : IRequestHandler<RollbackKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public RollbackKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(RollbackKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId && !a.IsDeleted, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate && !command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return Fail(403, "Only Admin can roll back a template version.");

        var version = await _uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.Id == command.ToVersionId, ct);

        if (version == null)
            return Fail(404, "Requested version not found.");

        var nextMajor = article.Version + 1;

        // Copy content to article
        article.Title = version.Title;
        article.Content = version.Content;
        article.Tags = version.Tags.ToList();
        article.Version = nextMajor;

        article.ReviewRequired = false;
        article.PendingReviewBy = null;
        article.ManagerRejectReason = null;
        article.Status = KbArticleStatusEnum.Published;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);

        // Also create a record for the newly restored version
        var restoredVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = nextMajor,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Approved,
            Title = article.Title,
            Content = article.Content,
            Tags = article.Tags.ToList(),
            ChangeDescription = $"Restored from version v{version.MajorVersion}.{version.MinorVersion}",
            ChangedBy = command.CurrentUserId
        };
        // Ô (nextMajor, 0) có thể đã bị chiếm bởi bản Pending sinh lúc khởi tạo bài viết (article.Version
        // vẫn là 0 trong khi row 1.0 đã tồn tại) — xem KbArticleVersionSlot. AddAsync thẳng là 23505 → 500.
        await KbArticleVersionSlot.UpsertAsync(_uow, restoredVersion, ct);
        await KbArticleVersionSlot.RejectOtherPendingAsync(
            _uow, article.Id, nextMajor, 0, "Article has been rolled back to a different version.", ct);

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Article has been rolled back to version v{version.MajorVersion}.{version.MinorVersion}.",
            Data = new KbArticleActionDTO
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
