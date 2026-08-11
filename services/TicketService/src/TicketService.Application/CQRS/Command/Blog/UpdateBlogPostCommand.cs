using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class UpdateBlogPostCommand : IRequest<CommonResponse<BlogPostActionDTO>>, IValidatable<CommonResponse<BlogPostActionDTO>>
{
    [JsonIgnore]
    public Guid BlogPostId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }

    /// <summary>Optimistic concurrency check — phải khớp với BlogPost.CurrentVersion.</summary>
    public int CurrentVersion { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<BlogPostActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogPostActionDTO>();

        if (BlogPostId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "BlogPostId", Detail = "Invalid post ID." });

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title is required." });
        else if (Title.Length > 256)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title must be at most 256 characters." });

        if (string.IsNullOrWhiteSpace(Slug))
            response.ListErrors.Add(new Errors { Field = "Slug", Detail = "Slug is required." });

        if (string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary is required." });

        if (string.IsNullOrWhiteSpace(ContentHtml))
            response.ListErrors.Add(new Errors { Field = "ContentHtml", Detail = "Content is required." });

        if (CurrentVersion <= 0)
            response.ListErrors.Add(new Errors { Field = "CurrentVersion", Detail = "Invalid version." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
