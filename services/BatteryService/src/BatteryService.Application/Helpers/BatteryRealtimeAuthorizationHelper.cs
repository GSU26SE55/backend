namespace BatteryService.Application.Helpers;

/// <summary>
/// Sprint BE-IoT-Realtime (#617) — logic role thuần (testable, không I/O) cho RBAC kênh realtime.
/// Pattern theo <c>TicketQueryHelper</c> (TicketService). Role so sánh case-insensitive.
/// </summary>
public static class BatteryRealtimeAuthorizationHelper
{
    public static bool HasRole(IReadOnlyCollection<string> roles, string role)
        => roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] candidates)
        => candidates.Any(c => HasRole(roles, c));

    public static bool IsManagerOrAdmin(IReadOnlyCollection<string> roles)
        => HasAnyRole(roles, "Admin", "Manager");

    /// <summary>
    /// Asset scope (chi tiết 1/nhiều pin): Admin/Manager OK;
    /// <b>Staff</b> xem được BẤT KỲ pin (MVP — Staff xử lý ticket/bảo trì theo pin cụ thể;
    /// chưa enforce "chỉ pin của ticket được giao" — đó là phần hardening sau, cần Ticket↔Asset);
    /// Customer chỉ khi sở hữu (CustomerId == actor).
    /// </summary>
    public static bool CanAccessAsset(Guid assetCustomerId, Guid actorUserId, IReadOnlyCollection<string> roles)
    {
        if (IsManagerOrAdmin(roles))
            return true;
        if (HasRole(roles, "Staff"))
            return true;   // MVP: Staff xem chi tiết bất kỳ pin
        if (HasRole(roles, "Customer"))
            return assetCustomerId == actorUserId;
        return false;
    }

    /// <summary>Customer scope: Admin/Manager OK; Customer chỉ chính mình (CustomerId == actor).</summary>
    public static bool CanAccessCustomer(Guid customerId, Guid actorUserId, IReadOnlyCollection<string> roles)
    {
        if (IsManagerOrAdmin(roles))
            return true;
        return HasRole(roles, "Customer") && customerId == actorUserId;
    }

    /// <summary>Site scope: chỉ Admin/Manager (Customer không xem theo site).</summary>
    public static bool CanAccessSite(IReadOnlyCollection<string> roles)
        => IsManagerOrAdmin(roles);
}
