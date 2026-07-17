using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogTemplateByIdQuery : IRequest<CommonResponse<BlogTemplateDTO>>
{
    public Guid TemplateId { get; set; }
}
