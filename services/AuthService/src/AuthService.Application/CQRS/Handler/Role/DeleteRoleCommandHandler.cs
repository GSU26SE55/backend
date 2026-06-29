using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Role;

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public DeleteRoleCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<RoleActionResponse> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
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
                Message = "Không thể xóa role hệ thống."
            };
        }

        var inUse = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.RoleId == request.Id && !a.IsDeleted, cancellationToken);

        if (inUse)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Role đang được gán cho ít nhất 1 tài khoản, không thể xóa."
            };
        }

        _unitOfWork.Roles.DeleteAsync(role);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleDeleted, null, true,
            Metadata: new Dictionary<string, object?> { ["roleId"] = role.Id, ["roleName"] = role.Name }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa role thành công.",
            Data = role.Id
        };
    }
}
