using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class DeleteBlogPostCommand : IRequest<CommonResponse<BlogPostActionDTO>>, IValidatable<CommonResponse<BlogPostActionDTO>>
{
    [JsonIgnore]
    public Guid BlogPostId { get; set; }

    public Task<CommonResponse<BlogPostActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogPostActionDTO>();

        if (BlogPostId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors { Field = "BlogPostId", Detail = "ID bài viết không hợp lệ." });
        }

        return Task.FromResult(response);
    }
}
