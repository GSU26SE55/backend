using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BatteryService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint BE-IoT-Realtime (#617) — <see cref="IBatteryRealtimeAuthorizationService"/> (§34.10.6).
/// - Asset (1 hoặc nhiều): Admin/Manager OK; Customer phải sở hữu TẤT CẢ pin trong list.
/// - Customer: chính mình (hoặc Admin/Manager).
/// - Site / BatteryType / All / SiteNone: chỉ Admin/Manager (xuyên khách).
/// </summary>
public class BatteryRealtimeAuthorizationService : IBatteryRealtimeAuthorizationService
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public BatteryRealtimeAuthorizationService(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> CanAccessScopeAsync(
        TelemetryScope scope,
        Guid actorUserId,
        IReadOnlyCollection<string> actorRoles,
        CancellationToken cancellationToken = default)
    {
        var isManagerOrAdmin = BatteryRealtimeAuthorizationHelper.IsManagerOrAdmin(actorRoles);

        switch (scope.Kind)
        {
            // Scope rộng / xuyên khách — chỉ Admin/Manager.
            case TelemetryScopeType.All:
            case TelemetryScopeType.SiteNone:
            case TelemetryScopeType.BatteryType:
            case TelemetryScopeType.Site:
                return isManagerOrAdmin;

            case TelemetryScopeType.Customer:
                return BatteryRealtimeAuthorizationHelper.CanAccessCustomer(scope.Ids[0], actorUserId, actorRoles);

            case TelemetryScopeType.Asset:
                if (isManagerOrAdmin)
                    return true;
                // Staff (MVP): xem chi tiết BẤT KỲ pin (xử lý ticket/bảo trì) — không cần truy DB.
                if (BatteryRealtimeAuthorizationHelper.HasRole(actorRoles, "Staff"))
                    return true;
                if (!BatteryRealtimeAuthorizationHelper.HasRole(actorRoles, "Customer"))
                    return false;

                // Customer: phải sở hữu MỌI pin trong list (và pin phải tồn tại).
                var owners = await _unitOfWork.BatteryAssets.GetAllAsync()
                    .AsNoTracking()
                    .Where(a => scope.Ids.Contains(a.Id) && !a.IsDeleted)
                    .Select(a => a.CustomerId)
                    .ToListAsync(cancellationToken);

                return owners.Count == scope.Ids.Count
                       && owners.All(customerId => customerId == actorUserId);

            default:
                return false;
        }
    }
}
