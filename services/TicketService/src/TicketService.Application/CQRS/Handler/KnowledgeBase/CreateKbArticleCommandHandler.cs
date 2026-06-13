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

public class CreateKbArticleCommandHandler : IRequestHandler<CreateKbArticleCommand, CommonResponse<KbArticleDto>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbCodeGenerator _codeGenerator;

    public CreateKbArticleCommandHandler(ITicketUnitOfWork uow, IKbCodeGenerator codeGenerator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
    }

    public async Task<CommonResponse<KbArticleDto>> Handle(CreateKbArticleCommand command, CancellationToken ct)
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
            Status = KbArticleStatusEnum.Draft,
            Version = 1,
            ViewCount = 0,
            HelpfulCount = 0,
            CreatedByUserId = command.CurrentUserId,
            ReviewRequired = false
        };

        await _uow.KnowledgeBaseArticles.AddAsync(article);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Bài viết đã được tạo thành công ở trạng thái Nháp.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }
}
