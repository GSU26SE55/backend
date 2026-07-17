using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogTemplateListQueryHandler : IRequestHandler<GetBlogTemplateListQuery, CommonResponse<List<BlogTemplateDTO>>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogTemplateListQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<List<BlogTemplateDTO>>> Handle(GetBlogTemplateListQuery request, CancellationToken ct)
    {
        var query = _uow.BlogTemplates.GetAllAsync().Where(x => !x.IsDeleted);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new BlogTemplateDTO
            {
                Id = x.Id.ToString(),
                Name = x.Name,
                Description = x.Description,
                ContentHtml = x.ContentHtml,
                IsActive = x.IsActive,
                CreatedByUserId = x.CreatedByUserId.ToString(),
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
            })
            .ToListAsync(ct);

        return new CommonResponse<List<BlogTemplateDTO>> { IsSuccess = true, StatusCode = 200, Data = items };
    }
}
