using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Blog;

public class UpdateBlogTemplateCommandHandler : IRequestHandler<UpdateBlogTemplateCommand, CommonResponse<BlogTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public UpdateBlogTemplateCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogTemplateDTO>> Handle(UpdateBlogTemplateCommand request, CancellationToken ct)
    {
        var template = await _uow.BlogTemplates.GetAllAsync()
            .Where(x => x.Id == request.TemplateId && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (template == null)
            return new CommonResponse<BlogTemplateDTO> { IsSuccess = false, StatusCode = 404, Message = "Template không tìm thấy." };

        template.Name = request.Name;
        template.Description = request.Description;
        template.ContentHtml = request.ContentHtml;
        template.IsActive = request.IsActive;

        _uow.BlogTemplates.UpdateAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<BlogTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật template thành công.",
            Data = new BlogTemplateDTO
            {
                Id = template.Id.ToString(),
                Name = template.Name,
                Description = template.Description,
                ContentHtml = template.ContentHtml,
                IsActive = template.IsActive,
                CreatedByUserId = template.CreatedByUserId.ToString(),
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt,
            }
        };
    }
}
