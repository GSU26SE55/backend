using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBase;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class CreateKbArticleCommandHandler : IRequestHandler<CreateKbArticleCommand, CommonResponse<KbArticleActionDto>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbCodeGenerator _codeGenerator;

    public CreateKbArticleCommandHandler(ITicketUnitOfWork uow, IKbCodeGenerator codeGenerator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
    }

    public async Task<CommonResponse<KbArticleActionDto>> Handle(CreateKbArticleCommand command, CancellationToken ct)
    {
        var code = await _codeGenerator.GenerateNextCodeAsync(ct);

        var article = new KnowledgeBaseArticle
        {
            Id = Guid.NewGuid(),
            Code = code,
            Category = command.Category,
            Title = command.Title,
            Symptoms = command.Symptoms,
            DiagnosisSteps = command.DiagnosisSteps,
            SolutionSteps = command.SolutionSteps,
            RecommendedParts = command.RecommendedParts,
            Tags = command.Tags ?? new List<string>(),
            Status = KbArticleStatusEnum.PendingReview,
            Version = 0,
            ViewCount = 0,
            HelpfulCount = 0,
            CreatedByUserId = command.CurrentUserId,
            ReviewRequired = true,
            PendingReviewBy = command.CurrentUserId,
            IsInternalOnly = command.IsInternalOnly
        };

        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = command.Title,
            Symptoms = command.Symptoms,
            DiagnosisSteps = command.DiagnosisSteps,
            SolutionSteps = command.SolutionSteps,
            RecommendedParts = command.RecommendedParts,
            Tags = command.Tags ?? new List<string>(),
            ChangeDescription = "Khởi tạo bài viết",
            ChangedBy = command.CurrentUserId
        };

        await _uow.KnowledgeBaseArticles.AddAsync(article);
        await _uow.KbArticleVersions.AddAsync(initialVersion);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Bài viết đã được khởi tạo và đang chờ phê duyệt (Version 1.0).",
            Data = new KbArticleActionDto
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }
}
