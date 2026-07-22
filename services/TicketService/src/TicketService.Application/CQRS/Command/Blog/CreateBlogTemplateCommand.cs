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
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Tên template không được để trống." });
        else if (Name.Length > 200)
            response.ListErrors.Add(new Errors { Field = "Name", Detail = "Tên template tối đa 200 ký tự." });

        if (string.IsNullOrWhiteSpace(ContentHtml))
            response.ListErrors.Add(new Errors { Field = "ContentHtml", Detail = "Nội dung template không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
