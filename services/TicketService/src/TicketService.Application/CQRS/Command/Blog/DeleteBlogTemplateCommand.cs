using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Command.Blog;

public class DeleteBlogTemplateCommand : IRequest<CommonResponse<BlogTemplateDTO>>, IValidatable<CommonResponse<BlogTemplateDTO>>
{
    [JsonIgnore]
    public Guid TemplateId { get; set; }

    public Task<CommonResponse<BlogTemplateDTO>> ValidateAsync()
    {
        var response = new CommonResponse<BlogTemplateDTO>();

        if (TemplateId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors { Field = "TemplateId", Detail = "ID template không hợp lệ." });
        }

        return Task.FromResult(response);
    }
}
