using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class RejectReviewCommand : IRequest<CommonResponse<KbArticleActionDto>>, IValidatable<CommonResponse<KbArticleActionDto>>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }
    public string? Reason { get; set; }

    public Task<CommonResponse<KbArticleActionDto>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDto>();

        if (ArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ArticleId", Detail = "ID bài viết không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Reason))
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Lý do từ chối không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
