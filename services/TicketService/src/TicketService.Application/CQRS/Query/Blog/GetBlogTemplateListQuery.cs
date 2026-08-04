using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Blog;

namespace TicketService.Application.CQRS.Query.Blog;

public class GetBlogTemplateListQuery : IRequest<CommonResponse<List<BlogTemplateDTO>>>
{
    public bool? IsActive { get; set; }
}
