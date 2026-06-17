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

public class ApproveReviewCommandHandler : IRequestHandler<ApproveReviewCommand, CommonResponse<KbArticleActionDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public ApproveReviewCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleActionDto>> Handle(ApproveReviewCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        if (article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Bài viết không ở trạng thái Chờ phê duyệt.");

        // Find the latest pending version to approve
        var nextMajor = article.Version + 1;
        var pendingVersion = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending)
            .OrderByDescending(v => v.MinorVersion)
            .FirstOrDefaultAsync(ct);

        if (pendingVersion != null)
        {
            // Copy contents to main article
            article.Title = pendingVersion.Title;
            article.Symptoms = pendingVersion.Symptoms;
            article.DiagnosisSteps = pendingVersion.DiagnosisSteps;
            article.SolutionSteps = pendingVersion.SolutionSteps;
            article.RecommendedParts = pendingVersion.RecommendedParts;
            article.Tags = pendingVersion.Tags.ToList();
            article.Version = nextMajor; // Update to new major version

            // Mark this version as approved
            pendingVersion.Status = KbVersionStatusEnum.Approved;
            _uow.KbArticleVersions.UpdateAsync(pendingVersion);

            // Optional: Mark other pending versions for this major version as rejected or obsolete
            var otherPendingVersions = await _uow.KbArticleVersions.GetAllAsync()
                .Where(v => v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending && v.Id != pendingVersion.Id)
                .ToListAsync(ct);

            foreach (var v in otherPendingVersions)
            {
                v.Status = KbVersionStatusEnum.Rejected;
                v.ManagerRejectReason = "Đã phê duyệt một phiên bản khác.";
                _uow.KbArticleVersions.UpdateAsync(v);
            }
        }

        article.Status = KbArticleStatusEnum.Published;
        article.ReviewRequired = false;
        article.PendingReviewBy = null;
        article.ManagerRejectReason = null;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Yêu cầu thay đổi đã được phê duyệt và cập nhật nội dung thành công.",
            Data = new KbArticleActionDto
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDto> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleActionDto>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
