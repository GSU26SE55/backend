using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

/// <summary>
/// Sao chép bài KB gốc → bài mới copy gần như toàn bộ (category/content/tags),
/// title thêm hậu tố "_copy", status = Draft, code mới. Trả Id để FE mở trang edit.
/// </summary>
public class DuplicateKbArticleCommandHandler
    : IRequestHandler<DuplicateKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbCodeGenerator _codeGenerator;

    public DuplicateKbArticleCommandHandler(ITicketUnitOfWork uow, IKbCodeGenerator codeGenerator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(
        DuplicateKbArticleCommand command,
        CancellationToken ct)
    {
        var source = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.SourceId, ct);

        if (source == null || source.IsDeleted)
            return Fail(404, "Không tìm thấy bài viết.");

        var code = await _codeGenerator.GenerateNextCodeAsync(ct);
        var contentText = KnowledgeBaseMapper.J(source.Content);
        var newId = Guid.NewGuid();
        var newTitle = $"{source.Title}_copy";

        var article = new KnowledgeBaseArticle
        {
            Id = newId,
            Code = code,
            Category = source.Category,
            Title = newTitle,
            Content = KnowledgeBaseMapper.ToJsonDoc(contentText),
            Tags = source.Tags.ToList(),
            Status = KbArticleStatusEnum.Draft,
            IsTemplate = false,
            Version = 0,
            ViewCount = 0,
            HelpfulCount = 0,
            CreatedByUserId = command.CurrentUserId,
            ReviewRequired = false,
            PendingReviewBy = null,
            IsInternalOnly = source.IsInternalOnly
        };

        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = newId,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = newTitle,
            Content = KnowledgeBaseMapper.ToJsonDoc(contentText),
            Tags = source.Tags.ToList(),
            ChangeDescription = $"Sao chép từ {source.Code}",
            ChangedBy = command.CurrentUserId
        };

        await _uow.KnowledgeBaseArticles.AddAsync(article);
        await _uow.KbArticleVersions.AddAsync(initialVersion);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Đã sao chép bài viết. Bản mới ở trạng thái Nháp.",
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
