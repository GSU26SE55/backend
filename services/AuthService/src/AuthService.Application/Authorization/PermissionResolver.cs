using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.Authorization;

/// <summary>
/// Helper resolve danh sách permission code mà 1 account có, dựa trên role hiện tại của account.
///
/// Quan hệ 1-N: mỗi account chỉ có duy nhất 1 role. Permission được resolve qua:
/// - Lấy <c>Account.RoleId</c>.
/// - Lấy Role.Status = Active.
/// - Lấy RolePermission của role đó (NOT IsDeleted).
/// - Lấy Permission tương ứng (NOT IsDeleted).
/// - Return distinct permission codes.
/// </summary>
public static class PermissionResolver
{
    public static async Task<List<string>> GetPermissionCodesAsync(
        IAuthUnitOfWork unitOfWork,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var query = from account in unitOfWork.Accounts.GetAllAsync()
                    where account.Id == accountId && !account.IsDeleted
                    join role in unitOfWork.Roles.GetAllAsync()
                        on account.RoleId equals role.Id
                    where role.Status == RoleStatusEnum.Active && !role.IsDeleted
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
