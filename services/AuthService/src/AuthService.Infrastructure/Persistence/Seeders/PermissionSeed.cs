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

        // Notification
        new(PermissionCodes.NotificationView, "Notification", "Xem thông báo"),
        new(PermissionCodes.NotificationSend, "Notification", "Gửi thông báo vận hành"),
        new(PermissionCodes.NotificationManageTemplate, "Notification", "Quản lý template thông báo"),
        // Sprint 6.4 NOTI4-10
        new(PermissionCodes.NotificationGroupView, "Notification", "Xem nhóm người nhận thông báo"),
        new(PermissionCodes.NotificationGroupManage, "Notification", "Tạo/sửa/xoá nhóm và thành viên"),
        new(PermissionCodes.NotificationBroadcast, "Notification", "Gửi thông báo hàng loạt cho nhóm"),
        new(PermissionCodes.NotificationBatchView, "Notification", "Xem lịch sử các lần gửi hàng loạt"),

        // KnowledgeBase
        new(PermissionCodes.KnowledgeBaseView, "KnowledgeBase", "Xem bài viết knowledge base"),
        new(PermissionCodes.KnowledgeBaseCreate, "KnowledgeBase", "Tạo bài viết knowledge base"),
        new(PermissionCodes.KnowledgeBaseUpdate, "KnowledgeBase", "Cập nhật bài viết knowledge base"),
        new(PermissionCodes.KnowledgeBaseDelete, "KnowledgeBase", "Xóa bài viết knowledge base"),
        new(PermissionCodes.KnowledgeBasePublish, "KnowledgeBase", "Publish/unpublish bài viết knowledge base"),

        // Reports
        new(PermissionCodes.ReportsView, "Reports", "Xem báo cáo vận hành"),
        new(PermissionCodes.ReportsExport, "Reports", "Export báo cáo"),

        // Audit
        new(PermissionCodes.AuditView, "Audit", "Xem audit log"),

        // Alert–Ticket Saga ops — Sprint 5B #241
        new(PermissionCodes.TicketSagaView, "TicketSaga", "Xem danh sách Alert-Ticket Saga + state hiện tại"),
        new(PermissionCodes.TicketSagaReprocess, "TicketSaga", "Reprocess Saga đang Failed (admin only)"),

        // Chat — Sprint Chat Phase 2 #516
        new(PermissionCodes.ChatCreatePublic, "Chat", "Tạo bình luận công khai trên ticket"),
        new(PermissionCodes.ChatCreateInternal, "Chat", "Tạo bình luận nội bộ (ẩn với Customer)"),
        new(PermissionCodes.ChatEditOwn, "Chat", "Sửa bình luận của chính mình"),
        new(PermissionCodes.ChatEditAny, "Chat", "Sửa bình luận của bất kỳ ai"),
        new(PermissionCodes.ChatDeleteOwn, "Chat", "Xóa bình luận của chính mình"),
        new(PermissionCodes.ChatDeleteAny, "Chat", "Xóa bình luận của bất kỳ ai"),
        new(PermissionCodes.ChatPin, "Chat", "Pin/unpin bình luận"),
        new(PermissionCodes.ChatViewInternal, "Chat", "Xem bình luận nội bộ"),
        new(PermissionCodes.ChatTemplateCreateGlobal, "Chat", "Tạo chat template phạm vi Global"),
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
