using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Role;

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public DeleteRoleCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

        var inUse = await _unitOfWork.AccountRoles
            .GetAllAsync()
            .AnyAsync(ar => ar.RoleId == request.Id && ar.IsActive, cancellationToken);

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
