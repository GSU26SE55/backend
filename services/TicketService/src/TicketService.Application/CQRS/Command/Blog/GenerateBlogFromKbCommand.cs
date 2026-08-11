using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class GenerateBlogFromKbCommand : IRequest<CommonResponse<BlogPostActionDTO>>, IValidatable<CommonResponse<BlogPostActionDTO>>
{
    [JsonIgnore]
    public Guid KbArticleId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<BlogPostActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogPostActionDTO>();

        if (KbArticleId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors { Field = "KbArticleId", Detail = "Invalid KB article ID." });
        }

        return Task.FromResult(response);
    }
}
