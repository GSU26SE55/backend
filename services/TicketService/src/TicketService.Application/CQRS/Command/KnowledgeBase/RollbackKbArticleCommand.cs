using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class RollbackKbArticleCommand : IRequest<CommonResponse<KbArticleActionDTO>>, IValidatable<CommonResponse<KbArticleActionDTO>>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }
    public Guid ToVersionId { get; set; }
    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<KbArticleActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDTO>();

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
