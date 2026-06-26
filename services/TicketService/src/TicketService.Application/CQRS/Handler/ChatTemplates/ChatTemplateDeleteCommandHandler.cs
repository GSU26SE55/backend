using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.ChatTemplateDelete;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.ChatTemplates;

public class ChatTemplateDeleteCommandHandler : IRequestHandler<ChatTemplateDeleteCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatTemplateDeleteCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(ChatTemplateDeleteCommand request, CancellationToken ct)
    {
        var template = await _uow.ChatTemplates.GetByIdAsync(request.TemplateId);
        if (template == null || template.IsDeleted)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy template." };

        var isAdmin = request.ActorRoles.Contains("Admin") || request.ActorRoles.Contains("Manager");
        if (!isAdmin && template.CreatedByUserId != request.ActorUserId)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 403, Message = "Không có quyền xóa template này." };

        _uow.ChatTemplates.DeleteAsync(template);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Xóa template thành công." };
    }
}
