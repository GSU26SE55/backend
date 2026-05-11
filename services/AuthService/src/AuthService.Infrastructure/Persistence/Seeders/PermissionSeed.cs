using AuthService.Application.Authorization;
using AuthService.Domain.Entities;

namespace AuthService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Định nghĩa system permissions + mapping role mặc định. Được consume bởi
/// <see cref="AuthDataSeeder"/> ở startup để đảm bảo DB luôn có đủ permission system.
/// </summary>
internal static class PermissionSeed
{
    /// <summary>Metadata cho 1 system permission cần seed.</summary>
    public sealed record SeedItem(string Code, string Module, string Description);

    public static readonly IReadOnlyList<SeedItem> All = new SeedItem[]
    {
        // User
        new(PermissionCodes.UserView, "User", "Xem danh sách + chi tiết account"),
        new(PermissionCodes.UserCreate, "User", "Tạo account trực tiếp"),
        new(PermissionCodes.UserUpdate, "User", "Cập nhật profile account"),
        new(PermissionCodes.UserDelete, "User", "Xóa mềm account"),
        new(PermissionCodes.UserChangeStatus, "User", "Đổi trạng thái account (Lock/Suspend/Ban)"),
        new(PermissionCodes.UserUnlock, "User", "Mở khóa account"),
        new(PermissionCodes.UserAssignRole, "User", "Gán/thu hồi role cho account"),
        new(PermissionCodes.UserForceLogout, "User", "Buộc account đăng xuất khỏi mọi session"),
        new(PermissionCodes.UserInvite, "User", "Gửi invite email cho user mới"),

        // Role
        new(PermissionCodes.RoleView, "Role", "Xem danh sách + chi tiết role"),
        new(PermissionCodes.RoleCreate, "Role", "Tạo role mới (non-system)"),
        new(PermissionCodes.RoleUpdate, "Role", "Cập nhật role"),
        new(PermissionCodes.RoleDelete, "Role", "Xóa role"),
        new(PermissionCodes.RoleAssignPermission, "Role", "Gán permission cho role"),

        // Battery
        new(PermissionCodes.BatteryView, "Battery", "Xem battery + sensor reading"),
        new(PermissionCodes.BatteryCreate, "Battery", "Tạo battery mới"),
        new(PermissionCodes.BatteryUpdate, "Battery", "Cập nhật battery"),
        new(PermissionCodes.BatteryDelete, "Battery", "Xóa battery"),
        new(PermissionCodes.BatteryAssign, "Battery", "Assign battery cho customer"),
        new(PermissionCodes.BatteryConfigure, "Battery", "Cấu hình ngưỡng cảnh báo"),

        // Ticket
        new(PermissionCodes.TicketView, "Ticket", "Xem ticket của chính mình"),
        new(PermissionCodes.TicketViewAll, "Ticket", "Xem mọi ticket trong hệ thống"),
        new(PermissionCodes.TicketCreate, "Ticket", "Tạo ticket"),
        new(PermissionCodes.TicketAssign, "Ticket", "Assign ticket cho Staff"),
        new(PermissionCodes.TicketResolve, "Ticket", "Mark ticket Resolved"),
        new(PermissionCodes.TicketClose, "Ticket", "Đóng ticket"),
        new(PermissionCodes.TicketEscalate, "Ticket", "Escalate ticket lên Manager"),

        // Audit
        new(PermissionCodes.AuditView, "Audit", "Xem audit log"),
    };

    /// <summary>Mapping role → list permission codes mặc định cho 4 system roles.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RoleDefaults =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ADMIN"] = All.Select(p => p.Code).ToList(),

            ["MANAGER"] = new[]
            {
                PermissionCodes.UserView, PermissionCodes.UserChangeStatus, PermissionCodes.UserUnlock,
                PermissionCodes.UserAssignRole, PermissionCodes.UserForceLogout,
                PermissionCodes.RoleView,
                PermissionCodes.BatteryView, PermissionCodes.BatteryAssign, PermissionCodes.BatteryConfigure,
                PermissionCodes.TicketViewAll, PermissionCodes.TicketAssign,
                PermissionCodes.TicketClose, PermissionCodes.TicketEscalate,
                PermissionCodes.AuditView,
            },

            ["STAFF"] = new[]
            {
                PermissionCodes.UserView,
                PermissionCodes.BatteryView, PermissionCodes.BatteryUpdate,
                PermissionCodes.TicketView, PermissionCodes.TicketResolve,
            },

            ["CUSTOMER"] = new[]
            {
                PermissionCodes.BatteryView,
                PermissionCodes.TicketView, PermissionCodes.TicketCreate,
            }
        };

    public static Permission BuildEntity(SeedItem item, Guid id)
    {
        return new Permission
        {
            Id = id,
            Code = item.Code,
            Module = item.Module,
            Description = item.Description,
            IsSystemPermission = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsDeleted = false
        };
    }
}
