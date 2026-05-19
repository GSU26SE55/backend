using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi admin tạo account ở chế độ invite (không set password sẵn).
/// EmailService consume để gửi email mời kèm link cấu hình từ <c>AdminInvite:AcceptUrlBase</c>
/// hoặc <c>Frontend:AcceptInviteUrl</c>, rồi append query <c>?token={InvitationToken}</c>.
///
/// Email nội dung mẫu:
///   "Bạn được mời tham gia hệ thống ABC với role <c>{Role}</c>. Click link để kích hoạt và đặt mật khẩu.
///    Link hết hạn lúc <c>{ExpiresAt}</c>."
///
/// Lưu ý: quan hệ Role ↔ Account là 1-N — mỗi account chỉ có duy nhất 1 role.
/// </summary>
public record SendAdminInviteEvent(
    Guid AccountId,
    string Email,
    string FullName,
    string Role,
    string InvitationToken,
    DateTime ExpiresAt
) : IntegrationEvent;
