using System.Collections.Concurrent;
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
///
/// #AUTH-16: cache theo <c>RoleId</c> trong process-memory với TTL <see cref="CacheTtl"/>.
/// Cache invalidation qua <see cref="InvalidateRole"/> hoặc <see cref="InvalidateAll"/>
/// (gọi từ PermissionsChangedEvent consumer — #AUTH-15).
/// </summary>
public static class PermissionResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly ConcurrentDictionary<Guid, CacheEntry> RolePermissionCache = new();

    public static async Task<List<string>> GetPermissionCodesAsync(
        IAuthUnitOfWork unitOfWork,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var roleInfo = await (from account in unitOfWork.Accounts.GetAllAsync()
                              where account.Id == accountId && !account.IsDeleted
                              join role in unitOfWork.Roles.GetAllAsync()
                                  on account.RoleId equals role.Id
                              where role.Status == RoleStatusEnum.Active && !role.IsDeleted
                              select new { role.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (roleInfo is null)
            return new List<string>();

        var roleId = roleInfo.Id;
        var now = DateTime.UtcNow;

        if (RolePermissionCache.TryGetValue(roleId, out var entry) && entry.ExpiresAt > now)
            return new List<string>(entry.Codes);

        var codes = await (from rolePermission in unitOfWork.RolePermissions.GetAllAsync()
                           where rolePermission.RoleId == roleId
                           join permission in unitOfWork.Permissions.GetAllAsync()
                               on rolePermission.PermissionId equals permission.Id
                           select permission.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        RolePermissionCache[roleId] = new CacheEntry(codes, now.Add(CacheTtl));
        return codes;
    }

    /// <summary>Invalidate cache cho 1 role (gọi từ PermissionsChangedEvent consumer).</summary>
    public static void InvalidateRole(Guid roleId) => RolePermissionCache.TryRemove(roleId, out _);

    /// <summary>Invalidate toàn bộ cache (dùng khi bulk seed/migration permissions).</summary>
    public static void InvalidateAll() => RolePermissionCache.Clear();

    private sealed record CacheEntry(IReadOnlyList<string> Codes, DateTime ExpiresAt);
}
