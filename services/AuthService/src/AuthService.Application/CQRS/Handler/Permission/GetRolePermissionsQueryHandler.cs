using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Permission;
using AuthService.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Permission;

public class GetRolePermissionsQueryHandler : IRequestHandler<GetRolePermissionsQuery, RolePermissionsResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetRolePermissionsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RolePermissionsResponse> Handle(GetRolePermissionsQuery request, CancellationToken cancellationToken)
    {
        if (request.RoleId == Guid.Empty)
        {
            return new RolePermissionsResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "RoleId không hợp lệ."
            };
        }

        var roleExists = await _unitOfWork.Roles
            .GetAllAsync()
            .AnyAsync(r => r.Id == request.RoleId && !r.IsDeleted, cancellationToken);

        if (!roleExists)
        {
            return new RolePermissionsResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Role không tồn tại."
            };
        }

        var query = from rolePermission in _unitOfWork.RolePermissions.GetAllAsync()
                    where rolePermission.RoleId == request.RoleId && !rolePermission.IsDeleted
                    join permission in _unitOfWork.Permissions.GetAllAsync()
                        on rolePermission.PermissionId equals permission.Id
                    orderby permission.Module, permission.Code
                    select new PermissionDto
                    {
                        Id = permission.Id,
                        Code = permission.Code,
                        Module = permission.Module,
                        Description = permission.Description,
                        IsSystemPermission = permission.IsSystemPermission,
                        CreatedAt = permission.CreatedAt
                    };

        var items = await query.AsNoTracking().ToListAsync(cancellationToken);

        return new RolePermissionsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = items
        };
    }
}
