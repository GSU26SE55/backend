using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBase;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class RollbackKbArticleCommand : IRequest<CommonResponse<KbArticleActionDto>>, IValidatable<CommonResponse<KbArticleActionDto>>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }
    public Guid ToVersionId { get; set; }
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<KbArticleActionDto>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDto>();

        if (ArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ArticleId", Detail = "ID bài viết không hợp lệ." });

        if (ToVersionId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ToVersionId", Detail = "ID phiên bản không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
