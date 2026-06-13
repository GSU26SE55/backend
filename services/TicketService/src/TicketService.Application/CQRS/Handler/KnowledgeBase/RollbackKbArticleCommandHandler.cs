using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class RollbackKbArticleCommandHandler : IRequestHandler<RollbackKbArticleCommand, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public RollbackKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(RollbackKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        var version = await _uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.Id == command.ToVersionId, ct);

        if (version == null)
            return Fail(404, "Không tìm thấy phiên bản yêu cầu.");

        // Instead of directly updating the main article, rolling back is essentially creating a new pending version
        // that copies the content of an old version, which then awaits manager approval.
        // Wait, the Rollback endpoint is an Admin/Manager endpoint. They can approve it directly.
        // The plan says "Hoàn tác nội dung bài viết về một phiên bản cũ trong lịch sử. Cập nhật bảng chính: Version++".
        // Let's create a new snapshot of the *current* state before overwriting, just in case.

        var nextMajor = article.Version + 1;
        var currentSnapshot = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = article.Version, // Snapshot of what it WAS
            MinorVersion = 0, // 0 can signify a snapshot or backup
            Status = KbVersionStatusEnum.Archived, // Archived state
            Title = article.Title,
            Symptoms = article.Symptoms,
            DiagnosisSteps = article.DiagnosisSteps,
            SolutionSteps = article.SolutionSteps,
            RecommendedParts = article.RecommendedParts,
            Tags = article.Tags.ToList(),
            ChangeDescription = $"Sao lưu trước khi hoàn tác về v{version.MajorVersion}.{version.MinorVersion}",
            ChangedBy = command.CurrentUserId
        };
        await _uow.KbArticleVersions.AddAsync(currentSnapshot);

        // Copy snapshot content to article
        article.Title = version.Title;
        article.Symptoms = version.Symptoms;
        article.DiagnosisSteps = version.DiagnosisSteps;
        article.SolutionSteps = version.SolutionSteps;
        article.RecommendedParts = version.RecommendedParts;
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
            Symptoms = article.Symptoms,
            DiagnosisSteps = article.DiagnosisSteps,
            SolutionSteps = article.SolutionSteps,
            RecommendedParts = article.RecommendedParts,
            Tags = article.Tags.ToList(),
            ChangeDescription = $"Khôi phục từ phiên bản v{version.MajorVersion}.{version.MinorVersion}",
            ChangedBy = command.CurrentUserId
        };
        await _uow.KbArticleVersions.AddAsync(restoredVersion);

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Bài viết đã được hoàn tác về phiên bản v{version.MajorVersion}.{version.MinorVersion}.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static CommonResponse<KbArticleDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
