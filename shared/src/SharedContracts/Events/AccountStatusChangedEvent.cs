using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Tài khoản đổi trạng thái bên AuthService.
///
/// <para><b>⚠️ <paramref name="OldStatus"/> và <paramref name="NewStatus"/> là số của
/// <c>AuthService.Domain.Enums.AccountStatusEnum</c></b> — PendingVerification=0, Active=1,
/// Locked=2, Inactive=3, Suspended=4, Banned=5. Enum trạng thái tài khoản của các service tiêu thụ
/// KHÔNG nhất thiết đánh số giống (TicketService bắt đầu từ 1, tức lệch một bậc), nên
/// <b>tuyệt đối không ép kiểu thẳng</b> <c>(AccountStatusEnum)evt.NewStatus</c> — phải map tường
/// minh. TicketService đã trả giá cho lỗi này: Locked(2) của Auth rơi trúng Active(2) của Ticket,
/// khoá tài khoản xong bên kia vẫn coi là hợp lệ để giao ticket, không log không exception.</para>
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
    bool IsActive = false
) : IntegrationEvent;
