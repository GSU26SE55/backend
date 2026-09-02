using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// Khoá lại phép dịch trạng thái tài khoản từ AuthService sang enum của TicketService.
///
/// <para>Hai enum phải giữ cùng wire contract 0..5. Mapper vẫn bảo vệ boundary trước giá trị lạ
/// từ publisher mới hơn.</para>
/// </summary>
public class AuthAccountStatusMapperTests
{
    [Fact]
    public void TicketStatusValues_MatchAuthServiceWireContract()
    {
        ((int)AccountStatusEnum.PendingVerification).Should().Be(0);
        ((int)AccountStatusEnum.Active).Should().Be(1);
        ((int)AccountStatusEnum.Locked).Should().Be(2);
        ((int)AccountStatusEnum.Inactive).Should().Be(3);
        ((int)AccountStatusEnum.Suspended).Should().Be(4);
        ((int)AccountStatusEnum.Banned).Should().Be(5);
    }

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
        // Khoá lại semantic quan trọng nhất của eligibility giao ticket.
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
