using BatteryService.Application.Realtime;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// Sprint BE-IoT-Realtime (#617) — RBAC mở stream SSE (§34.10.6).
/// Customer chỉ <c>asset</c> pin mình + <c>customer</c> chính mình; Manager/Admin theo <c>site</c> + bất kỳ asset.
/// Role string case-insensitive ("Admin"/"Manager"/"Staff"/"Customer"). Customer: UserId == CustomerId.
/// </summary>
public interface IBatteryRealtimeAuthorizationService
{
    /// <summary>True nếu actor được phép subscribe scope này.</summary>
    Task<bool> CanAccessScopeAsync(
        TelemetryScope scope,
        Guid actorUserId,
        IReadOnlyCollection<string> actorRoles,
        CancellationToken cancellationToken = default);
}
