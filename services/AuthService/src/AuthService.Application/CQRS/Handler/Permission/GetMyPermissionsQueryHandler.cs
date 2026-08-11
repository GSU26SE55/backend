using AuthService.Application.CQRS.Query.Permission;
using AuthService.Application.DTOs.Response.Permission;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Permission;

/// <summary>
/// Resolve toàn bộ permission của role mà account hiện tại đang được gán.
///
/// Flow:
/// 1. Validate AccountId != Guid.Empty.
/// 2. Lookup Account (chưa bị xóa) → lấy RoleId + Role (Name, Status, IsDeleted).
/// 3. Phân biệt 3 trường hợp role:
///    - Không có role / Role đã xóa: 403 "Tài khoản chưa được gán role".
///    - Role tồn tại nhưng <c>Status != Active</c>: 200, Permissions rỗng + message giải thích.
///    - Role Active: query RolePermission JOIN Permission (chưa xóa), distinct + sort.
/// </summary>
public class GetMyPermissionsQueryHandler : IRequestHandler<GetMyPermissionsQuery, MyPermissionsResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;

    public GetMyPermissionsQueryHandler(IAuthUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<MyPermissionsResponse> Handle(GetMyPermissionsQuery request, CancellationToken cancellationToken)
    {
        if (request.AccountId == Guid.Empty)
        {
            return new MyPermissionsResponse
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Token does not contain a valid AccountId."
            };
        }

        // LEFT JOIN Account → Role: tách "account không tồn tại" với "account chưa có role".
        var accountInfo = await _unitOfWork.Accounts.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.Id == request.AccountId && !a.IsDeleted)
            .Select(a => new
            {
                a.RoleId,
                Role = _unitOfWork.Roles.GetAllAsync()
                    .Where(r => r.Id == a.RoleId && !r.IsDeleted)
                    .Select(r => new { r.Id, r.Name, r.Status })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (accountInfo is null)
        {
            return new MyPermissionsResponse
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Account does not exist or has been deleted."
            };
        }

        if (accountInfo.Role is null)
        {
            return new MyPermissionsResponse
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Account has not been assigned a role."
            };
        }

        if (accountInfo.Role.Status != RoleStatusEnum.Active)
        {
            return new MyPermissionsResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Role is currently inactive — no permissions apply.",
                Data = new MyPermissionsDto
                {
                    RoleId = accountInfo.Role.Id,
                    RoleName = accountInfo.Role.Name,
                    Permissions = new List<PermissionDto>()
                }
            };
        }

        var roleId = accountInfo.Role.Id;

        // Distinct trên PermissionId để đề phòng RolePermission trùng lặp (defensive).
        // OrderBy Module → Code để FE render dạng accordion theo module ổn định.
        var permissions = await (from rp in _unitOfWork.RolePermissions.GetAllAsync()
                                 where rp.RoleId == roleId && !rp.IsDeleted
                                 join p in _unitOfWork.Permissions.GetAllAsync()
                                     on rp.PermissionId equals p.Id
                                 where !p.IsDeleted
                                 select new PermissionDto
                                 {
                                     Id = p.Id,
                                     Code = p.Code,
                                     Module = p.Module,
                                     Description = p.Description,
                                     IsSystemPermission = p.IsSystemPermission,
                                     CreatedAt = p.CreatedAt
                                 })
            .Distinct()
            .OrderBy(p => p.Module)
            .ThenBy(p => p.Code)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new MyPermissionsResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new MyPermissionsDto
            {
                RoleId = accountInfo.Role.Id,
                RoleName = accountInfo.Role.Name,
                Permissions = permissions
            }
        };
    }
}
