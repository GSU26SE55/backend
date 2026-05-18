using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Role;

public class ChangeRoleStatusCommandHandler : IRequestHandler<ChangeRoleStatusCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public ChangeRoleStatusCommandHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RoleActionResponse> Handle(ChangeRoleStatusCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles.GetAllAsync()
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
