namespace AuthService.Application.Authorization;

/// <summary>
/// Tập hợp permission code system-defined cho dự án Solar Battery Maintenance.
/// Code dạng "module.action", lowercase. Khi thêm permission mới:
/// 1. Add const string ở đây.
/// 2. Add seed data trong <c>PermissionSeed</c> + mapping role trong <c>AuthDataSeeder</c>.
/// 3. KHÔNG xóa const đã release — chỉ có thể deprecate.
/// </summary>
public static class PermissionCodes
{
    // Account / User (user.*)
    public const string UserView = "user.view";
    public const string UserCreate = "user.create";
    public const string UserUpdate = "user.update";
    public const string UserDelete = "user.delete";
    public const string UserChangeStatus = "user.change_status";
    public const string UserUnlock = "user.unlock";
    public const string UserAssignRole = "user.assign_role";
    public const string UserForceLogout = "user.force_logout";
    public const string UserInvite = "user.invite";

    // Role / Permission (role.*)
    public const string RoleView = "role.view";
    public const string RoleCreate = "role.create";
    public const string RoleUpdate = "role.update";
    public const string RoleDelete = "role.delete";
    public const string RoleAssignPermission = "role.assign_permission";

    // Battery (battery.*)
    public const string BatteryView = "battery.view";
    public const string BatteryCreate = "battery.create";
    public const string BatteryUpdate = "battery.update";
    public const string BatteryDelete = "battery.delete";
    public const string BatteryAssign = "battery.assign";
    public const string BatteryConfigure = "battery.configure";

    // Ticket (ticket.*)
    public const string TicketView = "ticket.view";
    public const string TicketViewAll = "ticket.view_all";
    public const string TicketCreate = "ticket.create";
    public const string TicketAssign = "ticket.assign";
    public const string TicketResolve = "ticket.resolve";
    public const string TicketClose = "ticket.close";
    public const string TicketEscalate = "ticket.escalate";

    // Audit (audit.*)
    public const string AuditView = "audit.view";
}
