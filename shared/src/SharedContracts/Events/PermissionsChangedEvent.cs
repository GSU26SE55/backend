using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// AuthService publish khi role-permission mapping thay đổi (seed mới hoặc admin update).
/// Downstream services (TicketService, ApiGateway, FE permission cache) invalidate cache.
///
/// Sprint 5B #241 (xem overall.md §7.5bis).
/// </summary>
public record PermissionsChangedEvent(
    string ChangeKind,            // "Seeded" | "BoundToRole" | "UnboundFromRole"
    string RoleCode,
    string[] AffectedPermissionCodes,
    DateTime ChangedAt
) : IntegrationEvent;
