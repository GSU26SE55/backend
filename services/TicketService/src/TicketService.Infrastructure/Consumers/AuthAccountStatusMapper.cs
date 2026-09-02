using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Đổi giá trị trạng thái tài khoản từ <c>AccountStatusChangedEvent</c> (mang số của
/// <c>AuthService.Domain.Enums.AccountStatusEnum</c>) sang enum của TicketService.
///
/// <para>Hai enum hiện dùng cùng wire contract 0..5. Mapper vẫn được giữ ở boundary để liệt kê
/// tường minh các giá trị được hỗ trợ, tránh ép kiểu một số lạ thành enum không hợp lệ khi AuthService
/// bổ sung trạng thái mới mà TicketService chưa được cập nhật.</para>
///
/// <para>Giá trị lạ (enum Auth thêm thành viên mới mà bên này chưa biết) map về
/// <see cref="AccountStatusEnum.Inactive"/>: không hiểu thì coi là không dùng được, chứ không đoán
/// bừa thành Active.</para>
/// </summary>
public static class AuthAccountStatusMapper
{
    // Số của AuthService.Domain.Enums.AccountStatusEnum — chép sang đây vì TicketService không
    // tham chiếu assembly của AuthService (mỗi service một Domain riêng).
    private const int AuthPendingVerification = 0;
    private const int AuthActive = 1;
    private const int AuthLocked = 2;
    private const int AuthInactive = 3;
    private const int AuthSuspended = 4;
    private const int AuthBanned = 5;

    public static AccountStatusEnum FromAuthStatus(int authStatus) => authStatus switch
    {
        AuthPendingVerification => AccountStatusEnum.PendingVerification,
        AuthActive => AccountStatusEnum.Active,
        AuthLocked => AccountStatusEnum.Locked,
        AuthInactive => AccountStatusEnum.Inactive,
        AuthSuspended => AccountStatusEnum.Suspended,
        AuthBanned => AccountStatusEnum.Banned,
        _ => AccountStatusEnum.Inactive,
    };
}
