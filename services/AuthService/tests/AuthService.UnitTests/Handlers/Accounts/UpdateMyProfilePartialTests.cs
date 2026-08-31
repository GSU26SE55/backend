using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// Form profile trên mobile chỉ PUT fullName/phoneNumber/address. Khi handler ghi đè vô
/// điều kiện, ngày sinh và timezone user đặt trên web bị xoá sạch ngay lần đầu họ sửa hồ sơ
/// trên điện thoại — không lỗi, không dấu vết.
///
/// <para>Quy ước hiện tại: field vắng mặt = giữ nguyên. Muốn xoá ngày sinh phải nói rõ bằng
/// <c>ClearBirthDate</c>.</para>
/// </summary>
public class UpdateMyProfilePartialTests
{
    private static (UpdateMyProfileCommandHandler Handler, Account Account) Build()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "me@example.com",
            PasswordHash = "x",
            FullName = "Old Name",
            Status = AccountStatusEnum.Active,
            RoleId = Guid.NewGuid(),
            // Người dùng đã đặt các giá trị này từ web.
            Address = "1 Nguyen Hue",
            DateOfBirth = new DateTime(1998, 4, 2),
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UpdateMyProfileCommandHandler(
            uow.Object, new Mock<IMessageProducerService>().Object);

        return (handler, account);
    }

    /// <summary>PUT chỉ có tên (đúng những gì mobile gửi) không được đụng tới phần còn lại.</summary>
    [Fact]
    public async Task PartialUpdate_KeepsFieldsTheClientDidNotSend()
    {
        var (handler, account) = Build();

        var response = await handler.Handle(new UpdateMyProfileCommand
        {
            AccountId = account.Id,
            FullName = "New Name",
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        account.FullName.Should().Be("New Name", "đây là thứ client thực sự đổi");
        account.Address.Should().Be("1 Nguyen Hue", "không gửi thì phải giữ nguyên");
        account.DateOfBirth.Should().Be(new DateTime(1998, 4, 2));
    }

    /// <summary>Có gửi thì vẫn ghi đè — quy ước "giữ nguyên" không được biến field thành read-only.</summary>
    [Fact]
    public async Task ExplicitValues_AreApplied()
    {
        var (handler, account) = Build();

        await handler.Handle(new UpdateMyProfileCommand
        {
            AccountId = account.Id,
            FullName = "New Name",
            Address = "2 Le Loi",
            BirthDate = new DateTime(2000, 1, 1),
        }, CancellationToken.None);

        account.Address.Should().Be("2 Le Loi");
        account.DateOfBirth.Should().Be(new DateTime(2000, 1, 1));
    }

    /// <summary>Chuỗi rỗng là "xoá" có chủ đích — web gửi "" khi user xoá trắng ô địa chỉ.</summary>
    [Fact]
    public async Task EmptyString_ClearsAddress()
    {
        var (handler, account) = Build();

        await handler.Handle(new UpdateMyProfileCommand
        {
            AccountId = account.Id,
            FullName = "New Name",
            Address = "",
        }, CancellationToken.None);

        account.Address.Should().BeEmpty();
    }

    /// <summary>Xoá ngày sinh vẫn phải làm được — qua cờ riêng, không qua việc bỏ trống field.</summary>
    [Fact]
    public async Task ClearBirthDateFlag_ClearsTheStoredDate()
    {
        var (handler, account) = Build();

        await handler.Handle(new UpdateMyProfileCommand
        {
            AccountId = account.Id,
            FullName = "New Name",
            ClearBirthDate = true,
        }, CancellationToken.None);

        account.DateOfBirth.Should().BeNull();
    }
}
