using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.Blog;

public class GetBlogTemplateByIdQueryHandler : IRequestHandler<GetBlogTemplateByIdQuery, CommonResponse<BlogTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public GetBlogTemplateByIdQueryHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogTemplateDTO>> Handle(GetBlogTemplateByIdQuery request, CancellationToken ct)
    {
        var entity = await _uow.BlogTemplates.GetAllAsync()
            .Where(x => x.Id == request.TemplateId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (entity == null)
            return new CommonResponse<BlogTemplateDTO> { IsSuccess = false, StatusCode = 404, Message = "Template not found." };

        return new CommonResponse<BlogTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new BlogTemplateDTO
            {
                Id = entity.Id.ToString(),
                Name = entity.Name,
                Description = entity.Description,
                ContentHtml = KnowledgeBaseMapper.J(entity.ContentHtml),
                IsActive = entity.IsActive,
                CreatedByUserId = entity.CreatedByUserId.ToString(),
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
            }
        };
    }

}
