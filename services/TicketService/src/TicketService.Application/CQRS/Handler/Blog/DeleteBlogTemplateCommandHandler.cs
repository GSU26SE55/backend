using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;

namespace TicketService.Application.CQRS.Handler.Blog;

public class DeleteBlogTemplateCommandHandler : IRequestHandler<DeleteBlogTemplateCommand, CommonResponse<BlogTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public DeleteBlogTemplateCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogTemplateDTO>> Handle(DeleteBlogTemplateCommand request, CancellationToken ct)
    {
        var template = await _uow.BlogTemplates.GetAllAsync()
            .Where(x => x.Id == request.TemplateId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (template == null)
            return new CommonResponse<BlogTemplateDTO> { IsSuccess = false, StatusCode = 404, Message = "Template not found." };

        _uow.BlogTemplates.DeleteAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<BlogTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Template deleted successfully.",
            Data = new BlogTemplateDTO
            {
                Id = template.Id.ToString(),
                Name = template.Name,
                Description = template.Description,
                ContentHtml = KnowledgeBaseMapper.J(template.ContentHtml),
                IsActive = template.IsActive,
                CreatedByUserId = template.CreatedByUserId.ToString(),
                CreatedAt = template.CreatedAt,
            }
        };
    }

}
