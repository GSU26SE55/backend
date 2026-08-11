using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.KnowledgeBases;

namespace TicketService.Application.CQRS.Command.KnowledgeBase;

public class ArchiveKbArticleCommand : IRequest<CommonResponse<KbArticleActionDTO>>, IValidatable<CommonResponse<KbArticleActionDTO>>
{
    /// <summary>
    /// Article id.
    /// </summary>
    [JsonIgnore]
    public Guid ArticleId { get; set; }
    [JsonIgnore]
    public string CurrentUserRole { get; set; } = string.Empty;

    public Task<CommonResponse<KbArticleActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<KbArticleActionDTO>();

        if (ArticleId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "ArticleId", Detail = "Invalid article ID." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
