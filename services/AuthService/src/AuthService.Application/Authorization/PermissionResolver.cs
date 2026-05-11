using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.Authorization;

/// <summary>
/// Helper resolve danh sách permission code mà 1 account có, dựa trên các role active của account.
///
/// Logic:
/// - Lấy AccountRole của account WHERE IsActive AND (ExpiredAt is null OR ExpiredAt > now).
/// - Lấy Role.Status = Active.
/// - Lấy RolePermission.NOT IsDeleted.
/// - Lấy Permission.NOT IsDeleted.
/// - Return distinct permission codes.
/// </summary>
public static class PermissionResolver
{
    public static async Task<List<string>> GetPermissionCodesAsync(
        IAuthUnitOfWork unitOfWork,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var query = from accountRole in unitOfWork.AccountRoles.GetAllAsync()
                    where accountRole.AccountId == accountId
                          && accountRole.IsActive
                          && (accountRole.ExpiredAt == null || accountRole.ExpiredAt > now)
                    join role in unitOfWork.Roles.GetAllAsync()
                        on accountRole.RoleId equals role.Id
                    where role.Status == RoleStatusEnum.Active
                    join rolePermission in unitOfWork.RolePermissions.GetAllAsync()
                        on role.Id equals rolePermission.RoleId
                    join permission in unitOfWork.Permissions.GetAllAsync()
                        on rolePermission.PermissionId equals permission.Id
                    select permission.Code;

        var codes = await query
            .Distinct()
            .ToListAsync(cancellationToken);

        return codes;
    }
}
