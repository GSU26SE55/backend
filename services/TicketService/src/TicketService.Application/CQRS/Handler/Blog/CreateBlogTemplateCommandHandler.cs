using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.DTOs.Response.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.Blog;

public class CreateBlogTemplateCommandHandler : IRequestHandler<CreateBlogTemplateCommand, CommonResponse<BlogTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public CreateBlogTemplateCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<CommonResponse<BlogTemplateDTO>> Handle(CreateBlogTemplateCommand request, CancellationToken ct)
    {
        var template = new BlogTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            ContentHtml = request.ContentHtml,
            IsActive = true,
            CreatedByUserId = request.CurrentUserId,
        };

        await _uow.BlogTemplates.AddAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<BlogTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo template thành công.",
            Data = MapToDto(template)
        };
    }

    private static BlogTemplateDTO MapToDto(BlogTemplate t) => new()
    {
        Id = t.Id.ToString(),
        Name = t.Name,
        Description = t.Description,
        ContentHtml = t.ContentHtml,
        IsActive = t.IsActive,
        CreatedByUserId = t.CreatedByUserId.ToString(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };
}
