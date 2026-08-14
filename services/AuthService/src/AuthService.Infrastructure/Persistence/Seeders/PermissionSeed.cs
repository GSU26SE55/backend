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
        new(PermissionCodes.UserView, "User", "View account list + details"),
        new(PermissionCodes.UserCreate, "User", "Create account directly"),
        new(PermissionCodes.UserUpdate, "User", "Update account profile"),
        new(PermissionCodes.UserDelete, "User", "Soft delete account"),
        new(PermissionCodes.UserChangeStatus, "User", "Change account status (Lock/Suspend/Ban)"),
        new(PermissionCodes.UserUnlock, "User", "Unlock account"),
        new(PermissionCodes.UserAssignRole, "User", "Assign/revoke role for account"),
        new(PermissionCodes.UserForceLogout, "User", "Force account to log out of all sessions"),
        new(PermissionCodes.UserInvite, "User", "Send invite email to new user"),

        // Role
        new(PermissionCodes.RoleView, "Role", "View role list + details"),
        new(PermissionCodes.RoleCreate, "Role", "Create new role (non-system)"),
        new(PermissionCodes.RoleUpdate, "Role", "Update role"),
        new(PermissionCodes.RoleDelete, "Role", "Delete role"),
        new(PermissionCodes.RoleAssignPermission, "Role", "Assign permission to role"),

        // Battery
        new(PermissionCodes.BatteryView, "Battery", "View battery + sensor readings"),
        new(PermissionCodes.BatteryCreate, "Battery", "Create new battery"),
        new(PermissionCodes.BatteryUpdate, "Battery", "Update battery"),
        new(PermissionCodes.BatteryDelete, "Battery", "Delete battery"),
        new(PermissionCodes.BatteryAssign, "Battery", "Assign battery to customer"),
        new(PermissionCodes.BatteryConfigure, "Battery", "Configure alert thresholds"),

        // Ticket
        new(PermissionCodes.TicketView, "Ticket", "View own tickets"),
        new(PermissionCodes.TicketViewAll, "Ticket", "View all tickets in the system"),
        new(PermissionCodes.TicketCreate, "Ticket", "Create ticket"),
        new(PermissionCodes.TicketAssign, "Ticket", "Assign ticket to Staff"),
        new(PermissionCodes.TicketResolve, "Ticket", "Mark ticket Resolved"),
        new(PermissionCodes.TicketClose, "Ticket", "Close ticket"),
        new(PermissionCodes.TicketEscalate, "Ticket", "Escalate ticket to Manager"),

        // Notification
        new(PermissionCodes.NotificationView, "Notification", "View notifications"),
        new(PermissionCodes.NotificationSend, "Notification", "Send operational notifications"),
        new(PermissionCodes.NotificationManageTemplate, "Notification", "Manage notification templates"),
        // Sprint 6.4 NOTI4-10
        new(PermissionCodes.NotificationGroupView, "Notification", "View notification recipient groups"),
        new(PermissionCodes.NotificationGroupManage, "Notification", "Create/edit/delete groups and members"),
        new(PermissionCodes.NotificationBroadcast, "Notification", "Send bulk notifications to a group"),
        new(PermissionCodes.NotificationBatchView, "Notification", "View bulk send history"),

        // KnowledgeBase
        new(PermissionCodes.KnowledgeBaseView, "KnowledgeBase", "View knowledge base articles"),
        new(PermissionCodes.KnowledgeBaseCreate, "KnowledgeBase", "Create knowledge base article"),
        new(PermissionCodes.KnowledgeBaseUpdate, "KnowledgeBase", "Update knowledge base article"),
        new(PermissionCodes.KnowledgeBaseDelete, "KnowledgeBase", "Delete knowledge base article"),
        new(PermissionCodes.KnowledgeBasePublish, "KnowledgeBase", "Publish/unpublish knowledge base article"),

        // Reports
        new(PermissionCodes.ReportsView, "Reports", "View operational reports"),
        new(PermissionCodes.ReportsExport, "Reports", "Export reports"),

        // Audit
        new(PermissionCodes.AuditView, "Audit", "View audit log"),

        // Alert–Ticket Saga ops — Sprint 5B #241
        new(PermissionCodes.TicketSagaView, "TicketSaga", "View Alert-Ticket Saga list + current state"),
        new(PermissionCodes.TicketSagaReprocess, "TicketSaga", "Reprocess a Failed Saga (admin only)"),

        // Chat — Sprint Chat Phase 2 #516
        new(PermissionCodes.ChatCreatePublic, "Chat", "Create a public comment on a ticket"),
        new(PermissionCodes.ChatCreateInternal, "Chat", "Create an internal comment (hidden from Customer)"),
        new(PermissionCodes.ChatEditOwn, "Chat", "Edit own comment"),
        new(PermissionCodes.ChatEditAny, "Chat", "Edit any comment"),
        new(PermissionCodes.ChatDeleteOwn, "Chat", "Delete own comment"),
        new(PermissionCodes.ChatDeleteAny, "Chat", "Delete any comment"),
        new(PermissionCodes.ChatPin, "Chat", "Pin/unpin comment"),
        new(PermissionCodes.ChatViewInternal, "Chat", "View internal comments"),
        new(PermissionCodes.ChatTemplateCreateGlobal, "Chat", "Create a Global-scope chat template"),
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
                PermissionCodes.NotificationView, PermissionCodes.NotificationSend,
                // Sprint 6.4 NOTI4-10 — Manager chỉ ĐỌC: xem được nhóm và lịch sử gửi để đối chiếu,
                // nhưng không tạo nhóm và không gửi hàng loạt (§17.6.4.6 câu hỏi 2 — mặc định chỉ Admin).
                PermissionCodes.NotificationGroupView, PermissionCodes.NotificationBatchView,
                PermissionCodes.KnowledgeBaseView, PermissionCodes.KnowledgeBaseCreate,
                PermissionCodes.KnowledgeBaseUpdate, PermissionCodes.KnowledgeBasePublish,
                PermissionCodes.ReportsView, PermissionCodes.ReportsExport,
                PermissionCodes.AuditView,
                PermissionCodes.TicketSagaView, // Sprint 5B #241 — Manager read-only
                // Sprint Chat Phase 2 #516 — Manager có toàn quyền chat (giữ đúng hành vi isManagerOrAdmin cũ)
                PermissionCodes.ChatCreatePublic, PermissionCodes.ChatCreateInternal,
                PermissionCodes.ChatEditOwn, PermissionCodes.ChatEditAny,
                PermissionCodes.ChatDeleteOwn, PermissionCodes.ChatDeleteAny,
                PermissionCodes.ChatPin, PermissionCodes.ChatViewInternal,
                PermissionCodes.ChatTemplateCreateGlobal,
            },

            ["STAFF"] = new[]
            {
                PermissionCodes.UserView,
                PermissionCodes.BatteryView, PermissionCodes.BatteryUpdate,
                PermissionCodes.TicketView, PermissionCodes.TicketResolve,
                PermissionCodes.NotificationView,
                PermissionCodes.KnowledgeBaseView,
                // Sprint Chat Phase 2 #516 — Staff không có quyền "any"/template global (giữ đúng hành vi cũ)
                PermissionCodes.ChatCreatePublic, PermissionCodes.ChatCreateInternal,
                PermissionCodes.ChatEditOwn, PermissionCodes.ChatDeleteOwn,
                PermissionCodes.ChatPin, PermissionCodes.ChatViewInternal,
            },

            ["CUSTOMER"] = new[]
            {
                PermissionCodes.BatteryView,
                PermissionCodes.TicketView, PermissionCodes.TicketCreate,
                PermissionCodes.NotificationView,
                PermissionCodes.KnowledgeBaseView,
                // Sprint Chat Phase 2 #516 — Customer chỉ tạo/sửa/xóa chat public của chính mình
                PermissionCodes.ChatCreatePublic, PermissionCodes.ChatEditOwn, PermissionCodes.ChatDeleteOwn,
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
