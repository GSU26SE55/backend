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

public class UpdateKbArticleCommandHandler : IRequestHandler<UpdateKbArticleCommand, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;

    public UpdateKbArticleCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(UpdateKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Không tìm thấy bài viết.");

        // Check authorization
        var isCreator = article.CreatedByUserId == command.CurrentUserId;
        var isManagerOrAdmin = command.CurrentUserRole.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                               command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        if (!isCreator && !isManagerOrAdmin && !command.CurrentUserRole.Equals("Staff", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(403, "Bạn không có quyền cập nhật bài viết này.");
        }

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
            Symptoms = command.Symptoms,
            DiagnosisSteps = command.DiagnosisSteps,
            SolutionSteps = command.SolutionSteps,
            RecommendedParts = command.RecommendedParts,
            Tags = command.Tags ?? new List<string>(),
            ChangeDescription = command.ChangeDescription ?? "Staff cập nhật nội dung",
            ChangedBy = command.CurrentUserId
        };
        await _uow.KbArticleVersions.AddAsync(newVersion);

        // If it's the owner or a manager, they can choose to bypass review (implemented here as auto-approve if we wanted,
        // but let's keep it simple: any update creates a Pending version that needs approval to overwrite main table)

        article.ReviewRequired = true;
        article.Status = KbArticleStatusEnum.PendingReview;
        article.PendingReviewBy = command.CurrentUserId;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Bản thảo thay đổi đã được lưu và đang chờ phê duyệt.",
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
