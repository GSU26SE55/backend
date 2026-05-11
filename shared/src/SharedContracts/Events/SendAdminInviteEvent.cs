using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// Publish khi admin tạo account ở chế độ invite (không set password sẵn).
/// NotificationService consume để gửi email mời kèm link <c>{AcceptUrlBase}?token={InvitationToken}</c>.
///
/// Email nội dung mẫu:
///   "Bạn được mời tham gia hệ thống ABC với role <c>{Roles}</c>. Click link để kích hoạt và đặt mật khẩu.
///    Link hết hạn lúc <c>{ExpiresAt}</c>."
/// </summary>
public record SendAdminInviteEvent(
    Guid AccountId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    string InvitationToken,
    DateTime ExpiresAt
) : IntegrationEvent;
