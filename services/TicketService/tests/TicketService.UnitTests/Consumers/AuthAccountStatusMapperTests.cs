using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// Khoá lại phép dịch trạng thái tài khoản từ AuthService sang enum của TicketService.
///
/// <para>Hai enum lệch nhau một bậc (Auth bắt đầu từ 0, Ticket từ 1) nên ép kiểu thô dịch sai toàn
/// bộ. Nguy hiểm nhất là <c>Locked</c> của Auth (2) rơi trúng <c>Active</c> của Ticket (2): khoá
/// tài khoản xong bên này vẫn coi là hợp lệ để giao ticket, không log không exception. Đã đo được
/// trên môi trường chạy thật trước khi sửa.</para>
/// </summary>
public class AuthAccountStatusMapperTests
{
    [Theory]
    [InlineData(0, AccountStatusEnum.PendingVerification)]
    [InlineData(1, AccountStatusEnum.Active)]
    [InlineData(2, AccountStatusEnum.Locked)]
    [InlineData(3, AccountStatusEnum.Inactive)]
    [InlineData(4, AccountStatusEnum.Suspended)]
    [InlineData(5, AccountStatusEnum.Banned)]
    public void FromAuthStatus_MapsEveryKnownStatus(int authStatus, AccountStatusEnum expected)
    {
        AuthAccountStatusMapper.FromAuthStatus(authStatus).Should().Be(expected);
    }

    [Fact]
    public void FromAuthStatus_LockedNeverBecomesActive()
    {
        // Case này là lý do file mapper tồn tại — giữ riêng để khi ai đó "đơn giản hoá" mapper
        // thành phép cộng hay ép kiểu thì test đỏ ngay ở đúng chỗ đau.
        AuthAccountStatusMapper.FromAuthStatus(2).Should().NotBe(AccountStatusEnum.Active);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(99)]
    [InlineData(-1)]
    public void FromAuthStatus_UnknownValue_FallsBackToInactive(int authStatus)
    {
        // Auth thêm trạng thái mới mà bên này chưa biết thì coi là không dùng được, không đoán bừa
        // thành Active — sai theo hướng an toàn.
        AuthAccountStatusMapper.FromAuthStatus(authStatus).Should().Be(AccountStatusEnum.Inactive);
    }
}
