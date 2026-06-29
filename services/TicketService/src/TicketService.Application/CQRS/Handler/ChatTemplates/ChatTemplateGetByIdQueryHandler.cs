using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.ChatTemplates;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatTemplateGetByIdQueryHandler : IRequestHandler<ChatTemplateGetByIdQuery, CommonResponse<ChatTemplateDTO>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatTemplateGetByIdQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<ChatTemplateDTO>> Handle(ChatTemplateGetByIdQuery request, CancellationToken ct)
    {
        var template = await _uow.ChatTemplates.GetByIdAsync(request.TemplateId);

        if (template is null || template.IsDeleted)
        {
            return new CommonResponse<ChatTemplateDTO>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy template."
            };
        }

        var isAdmin = request.ActorRoles.Contains("Admin") || request.ActorRoles.Contains("Manager");

        // Personal templates: chỉ có chính chủ hoặc Admin/Manager mới được lấy.
        if (!isAdmin && template.Scope == ChatTemplateScopeEnum.Personal && template.CreatedByUserId != request.ActorUserId)
        {
            return new CommonResponse<ChatTemplateDTO>
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không có quyền truy cập tài nguyên này."
            };
        }

        return new CommonResponse<ChatTemplateDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = ChatTemplateCreateCommandHandler.ToDTO(template)
        };
    }
}
