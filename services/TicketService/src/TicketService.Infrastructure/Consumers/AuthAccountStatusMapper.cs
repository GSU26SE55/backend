using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

/// <summary>
/// Đổi giá trị trạng thái tài khoản từ <c>AccountStatusChangedEvent</c> (mang số của
/// <c>AuthService.Domain.Enums.AccountStatusEnum</c>) sang enum của TicketService.
///
/// <para><b>Vì sao phải có:</b> hai enum ĐÁNH SỐ LỆCH NHAU MỘT BẬC — AuthService bắt đầu từ
/// <c>PendingVerification = 0</c>, TicketService bắt đầu từ <c>PendingVerification = 1</c>. Ép kiểu
/// thô <c>(AccountStatusEnum)evt.NewStatus</c> nên dịch sai TOÀN BỘ trạng thái, và sai theo hướng
/// nguy hiểm nhất: <c>Locked</c> của Auth (2) rơi đúng vào <c>Active</c> của Ticket (2), tức là
/// khoá tài khoản bên Auth lại làm nó trở nên hợp lệ để giao ticket bên này.</para>
///
/// <para>Đã đo trên môi trường chạy thật: khoá tài khoản xong <c>last_synced_at</c> có cập nhật
/// (consumer chạy đúng) nhưng <c>status</c> đọc ra vẫn là Active — lỗi im lặng, không log, không
/// exception.</para>
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
