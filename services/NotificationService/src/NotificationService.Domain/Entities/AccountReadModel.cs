using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// GH-604 — Read-model account đồng bộ từ UserService qua message bus
/// (AccountActivated / AccountProfileUpdated / AccountDeleted). Dùng để resolve
/// recipient thật cho notification (vd broadcast toàn bộ Manager/Admin) thay cho
/// placeholder <see cref="System.Guid.Empty"/>.
///
/// <see cref="BaseEntity.Id"/> = AccountId bên AuthService (không tự sinh).
/// Mirror pattern CustomerAccount của BatteryService.
///
/// 02/08/2026 — bổ sung nguồn đồng bộ thứ tư: <c>AccountSyncSnapshotEvent</c>. Trước đó chỉ có 3
/// event vòng đời, mà không event nào mang thông tin đổi-role hay đổi-status, nên
/// <see cref="Role"/> và <see cref="IsActive"/> lệch vĩnh viễn sau mỗi lần admin đổi role/khoá
/// tài khoản; account tạo bằng seeder thì không bao giờ vào được bảng này. Snapshot chở đủ trạng
/// thái hiện tại và có thể phát lại bao nhiêu lần cũng được, nên vừa vá được drift vừa dùng làm
/// công cụ đối soát (<c>POST /api/admin/accounts/resync</c>).
/// </summary>
public class AccountReadModel : AuditableEntity
{
    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>Role hiện tại (single — quan hệ 1-N): "Admin" | "Manager" | "Staff" | "Customer".</summary>
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // 02/08/2026 — bỏ PreferredLocale. Trường này chỉ có một người dùng duy nhất là
    // NotificationDispatcher.ResolveLocaleAsync để chọn locale của template; template nay không còn
    // locale nên nó thành dữ liệu chết. Trên thực tế nó chưa bao giờ có giá trị: không consumer nào
    // ghi vào (UserService không publish trường này), nên mọi dòng đều đang null.

    public DateTime LastSyncedAtUtc { get; set; }

    /// <summary>
    /// Thời điểm (UTC) của <c>AccountSyncSnapshotEvent</c> gần nhất đã được áp vào dòng này —
    /// lấy từ <c>SnapshotAtUtc</c> của event, KHÔNG phải lúc consume.
    ///
    /// Dùng để chặn snapshot về trễ ghi đè snapshot mới: RabbitMQ không bảo đảm thứ tự, nên hai
    /// thao tác admin liền nhau (đổi role rồi khoá tài khoản) có thể tới ngược chiều và làm
    /// read-model lùi về trạng thái cũ.
    ///
    /// Không so sánh bằng <see cref="LastSyncedAtUtc"/> được: trường đó ghi thời điểm CONSUME và bị
    /// cả 3 consumer vòng đời ghi chung, nên nó luôn mới hơn mốc của event và sẽ loại nhầm mọi
    /// snapshot hợp lệ.
    ///
    /// <c>null</c> = dòng này chưa từng được snapshot đụng tới (chỉ mới đi qua 3 consumer vòng đời).
    /// </summary>
    public DateTime? LastSnapshotAtUtc { get; set; }
}
