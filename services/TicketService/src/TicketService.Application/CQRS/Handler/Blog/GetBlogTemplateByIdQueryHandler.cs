using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogTemplateByIdQueryHandler : IRequestHandler<GetBlogTemplateByIdQuery, CommonResponse<BlogTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogTemplateByIdQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogTemplateDTO>> Handle(GetBlogTemplateByIdQuery request, CancellationToken ct)
    {
        var template = await _uow.BlogTemplates.GetAllAsync()
            .Where(x => x.Id == request.TemplateId && !x.IsDeleted)
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
            .FirstOrDefaultAsync(ct);

        if (template == null)
            return new CommonResponse<BlogTemplateDTO> { IsSuccess = false, StatusCode = 404, Message = "Template không tìm thấy." };

        return new CommonResponse<BlogTemplateDTO> { IsSuccess = true, StatusCode = 200, Data = template };
    }
}
