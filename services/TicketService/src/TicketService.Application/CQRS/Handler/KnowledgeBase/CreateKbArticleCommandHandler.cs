using System.Text.Json;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class CreateKbArticleCommandHandler : IRequestHandler<CreateKbArticleCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbCodeGenerator _codeGenerator;

    public CreateKbArticleCommandHandler(ITicketUnitOfWork uow, IKbCodeGenerator codeGenerator)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(CreateKbArticleCommand command, CancellationToken ct)
    {
        if (command.IsTemplate && !command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return Fail(403, "Chỉ Admin mới có thể tạo template.");

        var code = await _codeGenerator.GenerateNextCodeAsync(ct);

        KbArticleStatusEnum initialStatus;
        bool reviewRequired;
        Guid? pendingReviewBy;
        string versionMessage;

        if (command.IsTemplate)
        {
            initialStatus = KbArticleStatusEnum.Draft;
            reviewRequired = false;
            pendingReviewBy = null;
            versionMessage = "Khởi tạo template";
        }
        else
        {
            initialStatus = KbArticleStatusEnum.PendingReview;
            reviewRequired = true;
            pendingReviewBy = command.CurrentUserId;
            versionMessage = "Khởi tạo bài viết";
        }

        var article = new KnowledgeBaseArticle
        {
            Id = Guid.NewGuid(),
            Code = code,
            Category = command.Category,
            Title = command.Title,
            Content = J(command.Content),
            Tags = command.Tags ?? new List<string>(),
            Status = initialStatus,
            IsTemplate = command.IsTemplate,
            Version = 0,
            ViewCount = 0,
            HelpfulCount = 0,
            CreatedByUserId = command.CurrentUserId,
            ReviewRequired = reviewRequired,
            PendingReviewBy = pendingReviewBy
        };

        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = command.Title,
            Content = J(command.Content),
            Tags = command.Tags ?? new List<string>(),
            ChangeDescription = versionMessage,
            ChangedBy = command.CurrentUserId
        };

        await _uow.KnowledgeBaseArticles.AddAsync(article);
        await _uow.KbArticleVersions.AddAsync(initialVersion);
        await _uow.SaveChangesAsync(ct);

        var message = command.IsTemplate
            ? "Template đã được khởi tạo ở trạng thái Nháp (Version 1.0)."
            : "Bài viết đã được khởi tạo và đang chờ phê duyệt (Version 1.0).";

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = message,
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

    private static JsonDocument J(string? v) => KnowledgeBaseMapper.ToJsonDoc(v);
}
