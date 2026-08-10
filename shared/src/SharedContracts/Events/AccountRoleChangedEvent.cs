using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// GH-769 — AuthService phát khi role của một account thay đổi.
/// </summary>
/// <remarks>
/// <para>
/// Trước đây <c>ChangeAccountRoleCommandHandler</c> chỉ ghi Auth DB + audit. Battery và Ticket
/// chỉ biết role đúng MỘT lần — lúc account được kích hoạt — nên đổi Customer ↔ Staff xong,
/// bản sao ở hai service kia giữ nguyên role cũ: thiếu <c>StaffAccount</c> nên không giao ticket
/// được, hoặc thừa <c>CustomerAccount</c> nên vẫn giữ quyền nghiệp vụ cũ.
/// </para>
/// <para>
/// <c>AccountSyncSnapshotEvent</c> KHÔNG thay thế được: chỉ NotificationService consume nó.
/// </para>
/// <para>
/// Mang theo cả <paramref name="OldRole"/> lẫn <paramref name="NewRole"/> để consumer biết bản
/// sao NÀO cần dọn — chỉ có role mới thì không suy ra được phải xoá phía nào.
/// Đủ trường hồ sơ (<paramref name="FullName"/>, <paramref name="PhoneNumber"/>) để consumer tạo
/// mới bản sao còn thiếu mà không phải gọi ngược về AuthService.
/// </para>
/// </remarks>
public record AccountRoleChangedEvent(
    Guid AccountId,
    string Email,
    string FullName,
    string? PhoneNumber,
    string OldRole,
    string NewRole,
    DateTime ChangedAtUtc
) : IntegrationEvent;
