namespace BatteryService.Application.Helpers;

/// <summary>
/// GH-722 — giới hạn dữ liệu theo tenant cho tầng REST.
///
/// Chính sách lấy NGUYÊN từ <see cref="BatteryRealtimeAuthorizationHelper"/> (spec §34.10.6)
/// để đường REST và đường SSE không trôi khỏi nhau:
/// <list type="bullet">
///   <item>Admin / Manager — toàn bộ dữ liệu.</item>
///   <item>Staff — mọi asset (quyết định MVP: Staff xử lý ticket/bảo trì trên pin bất kỳ).</item>
///   <item>Customer — chỉ tài nguyên có <c>CustomerId</c> trùng chính mình.</item>
///   <item>Còn lại (kể cả Customer mà token không đọc được id) — TỪ CHỐI.</item>
/// </list>
///
/// Logic thuần, không I/O ⇒ test được trực tiếp.
/// </summary>
public static class BatteryTenantScopeHelper
{
    public enum TenantScopeKind
    {
        /// <summary>Không giới hạn — Admin/Manager/Staff.</summary>
        Unrestricted = 1,

        /// <summary>Chỉ được thấy dữ liệu của <see cref="TenantScope.CustomerId"/>.</summary>
        Customer = 2,

        /// <summary>Không xác định được tenant ⇒ chặn (fail closed).</summary>
        Denied = 3,
    }

    public readonly record struct TenantScope(TenantScopeKind Kind, Guid CustomerId)
    {
        public bool IsUnrestricted => Kind == TenantScopeKind.Unrestricted;
        public bool IsDenied => Kind == TenantScopeKind.Denied;

        /// <summary>True khi phải lọc theo <see cref="CustomerId"/>.</summary>
        public bool IsCustomerScoped => Kind == TenantScopeKind.Customer;
    }

    /// <summary>
    /// Quy đổi (userId, roles) của caller thành phạm vi dữ liệu được phép thấy.
    /// </summary>
    /// <remarks>
    /// FAIL CLOSED có chủ ý: nếu caller mang role Customer nhưng <paramref name="userId"/>
    /// không parse được thành Guid khác rỗng thì trả <see cref="TenantScopeKind.Denied"/>,
    /// KHÔNG trả Unrestricted. Một token hỏng không được biến thành quyền xem tất cả.
    /// </remarks>
    public static TenantScope Resolve(string? userId, IReadOnlyCollection<string> roles)
    {
        roles ??= Array.Empty<string>();

        if (BatteryRealtimeAuthorizationHelper.IsManagerOrAdmin(roles)
            || BatteryRealtimeAuthorizationHelper.HasRole(roles, "Staff"))
        {
            return new TenantScope(TenantScopeKind.Unrestricted, Guid.Empty);
        }

        if (BatteryRealtimeAuthorizationHelper.HasRole(roles, "Customer")
            && Guid.TryParse(userId, out var customerId)
            && customerId != Guid.Empty)
        {
            return new TenantScope(TenantScopeKind.Customer, customerId);
        }

        return new TenantScope(TenantScopeKind.Denied, Guid.Empty);
    }
}
