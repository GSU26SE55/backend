using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class CreateBlogTemplateCommand : IRequest<CommonResponse<BlogTemplateDTO>>, IValidatable<CommonResponse<BlogTemplateDTO>>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;

    [JsonIgnore]
    public Guid CurrentUserId { get; set; }

    public Task<CommonResponse<BlogTemplateDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogTemplateDTO>();

        if (string.IsNullOrWhiteSpace(Name))
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Template name is required." });
        else if (Name.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Template name must be at most 200 characters." });

        if (string.IsNullOrWhiteSpace(ContentHtml))
            response.ListErrors.Add(new Errors { Field = "ContentHtml", Detail = "Template content is required." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
