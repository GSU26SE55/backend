using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Role;

public class ChangeRoleStatusCommandHandler : IRequestHandler<ChangeRoleStatusCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public ChangeRoleStatusCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<RoleActionResponse> Handle(ChangeRoleStatusCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken);
        if (role == null)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy role."
            };
        }

        if (role.IsSystemRole)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không thể đổi trạng thái role hệ thống."
            };
        }

        if (role.Status == request.Status)
        {
            return new RoleActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Trạng thái không thay đổi.",
                Data = role.Id
            };
        }

        role.Status = request.Status;
        _unitOfWork.Roles.UpdateAsync(role);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleStatusChanged, null, true,
            Metadata: new Dictionary<string, object?> { ["roleId"] = role.Id, ["status"] = request.Status.ToString() }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật trạng thái role thành công.",
            Data = role.Id
        };
    }
}
