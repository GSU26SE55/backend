using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class CreateBlogPostCommand : IRequest<CommonResponse<BlogPostActionDTO>>, IValidatable<CommonResponse<BlogPostActionDTO>>
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public Guid? BlogTemplateId { get; set; }

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<BlogPostActionDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogPostActionDTO>();

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title is required." });
        else if (Title.Length > 256)
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Title must be at most 256 characters." });

        if (string.IsNullOrWhiteSpace(Slug))
            response.ListErrors.Add(new Errors { Field = "Slug", Detail = "Slug is required." });
        else if (Slug.Length > 300)
            response.ListErrors.Add(new Errors { Field = "Slug", Detail = "Slug must be at most 300 characters." });
        else if (!BlogSlugPolicy.IsValid(Slug))
            response.ListErrors.Add(new Errors { Field = "Slug", Detail = BlogSlugPolicy.FormatMessage });

        if (string.IsNullOrWhiteSpace(Summary))
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary is required." });
        // Title (256) và Slug (300) đều có cap, riêng Summary thì không — summary 5000 ký tự
        // lưu được qua API rồi sau đó chặn chính editor lại.
        else if (Summary.Trim().Length > 1000)
            response.ListErrors.Add(new Errors { Field = "Summary", Detail = "Summary must be at most 1000 characters." });

        RichTextPolicy.AddContentErrors(response.ListErrors, ContentHtml, "ContentHtml", "Content");

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
