using BatteryService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Application.Helpers;

/// <summary>
/// GH-722 — kiểm tra quyền sở hữu tài nguyên theo tenant (có truy DB).
///
/// Tách khỏi <see cref="BatteryTenantScopeHelper"/> vì helper đó là logic thuần không I/O.
/// Ở đây là phần phải hỏi DB: "asset/site này có thuộc customer đang gọi không?".
/// </summary>
public static class BatteryTenantAccessGuard
{
    /// <summary>
    /// True nếu caller được phép đọc dữ liệu gắn với <paramref name="batteryAssetId"/>.
    /// Admin/Manager/Staff luôn true; Customer chỉ khi sở hữu asset (và asset chưa bị xoá mềm).
    /// </summary>
    public static async Task<bool> CanAccessAssetAsync(
        IBatteryUnitOfWork unitOfWork,
        Guid batteryAssetId,
        BatteryTenantScopeHelper.TenantScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsDenied)
            return false;
        if (scope.IsUnrestricted)
            return true;

        return await unitOfWork.BatteryAssets
            .GetAllAsync()
            .AsNoTracking()
            .AnyAsync(
                asset => asset.Id == batteryAssetId
                         && !asset.IsDeleted
                         && asset.CustomerId == scope.CustomerId,
                cancellationToken);
    }

    /// <summary>
    /// True nếu caller được phép đọc dữ liệu gắn với <paramref name="siteId"/>.
    /// Admin/Manager/Staff luôn true; Customer chỉ khi site thuộc mình.
    /// </summary>
    public static async Task<bool> CanAccessSiteAsync(
        IBatteryUnitOfWork unitOfWork,
        Guid siteId,
        BatteryTenantScopeHelper.TenantScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsDenied)
            return false;
        if (scope.IsUnrestricted)
            return true;

        return await unitOfWork.Sites
            .GetAllAsync()
            .AsNoTracking()
            .AnyAsync(
                site => site.Id == siteId
                        && !site.IsDeleted
                        && site.CustomerId == scope.CustomerId,
                cancellationToken);
    }
}
