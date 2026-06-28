using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatTemplateUpdateCommandHandler : IRequestHandler<ChatTemplateUpdateCommand, CommonResponse<ChatTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatTemplateUpdateCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<ChatTemplateDTO>> Handle(ChatTemplateUpdateCommand request, CancellationToken ct)
    {
        var template = await _uow.ChatTemplates.GetByIdAsync(request.TemplateId);
        if (template == null || template.IsDeleted)
            return new CommonResponse<ChatTemplateDTO> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy template." };

        var isAdmin = request.ActorRoles.Contains("Admin") || request.ActorRoles.Contains("Manager");
        if (!isAdmin && template.CreatedByUserId != request.ActorUserId)
            return new CommonResponse<ChatTemplateDTO> { IsSuccess = false, StatusCode = 403, Message = "Không có quyền sửa template này." };

        if (request.Name != null)
            template.Name = request.Name.Trim();

        if (request.Content != null)
            template.Content = request.Content;

        if (request.Category.HasValue)
            template.Category = request.Category.Value;

        if (request.IsInternalDefault.HasValue)
            template.IsInternalDefault = request.IsInternalDefault.Value;

        if (request.IsActive.HasValue)
            template.IsActive = request.IsActive.Value;

        _uow.ChatTemplates.UpdateAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<ChatTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = ChatTemplateCreateCommandHandler.ToDTO(template)
        };
    }
}
