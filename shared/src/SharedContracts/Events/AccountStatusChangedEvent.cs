using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Tài khoản đổi trạng thái bên AuthService.
///
/// <para><b>⚠️ <paramref name="OldStatus"/> và <paramref name="NewStatus"/> là số của
/// <c>AuthService.Domain.Enums.AccountStatusEnum</c></b> — PendingVerification=0, Active=1,
/// Locked=2, Inactive=3, Suspended=4, Banned=5. TicketService hiện đồng bộ đúng cùng contract số
/// này. Consumer vẫn phải validate/map tường minh thay vì ép kiểu thẳng để một trạng thái Auth mới,
/// chưa được service tiêu thụ hỗ trợ, không trở thành enum không hợp lệ.</para>
/// <para><c>AvatarUrl</c> là optional để giữ tương thích với publisher cũ. Khi null, consumer phải
/// giữ nguyên giá trị đang lưu thay vì hiểu thành lệnh xoá avatar.</para>
/// </summary>
public record AccountStatusChangedEvent(
    Guid AccountId,
    string Email,
    int OldStatus,
    int NewStatus,
    string? Reason,
    string Role = "",
    string FullName = "",
    string? PhoneNumber = null,
    bool IsActive = false,
    string? AvatarUrl = null
) : IntegrationEvent;
