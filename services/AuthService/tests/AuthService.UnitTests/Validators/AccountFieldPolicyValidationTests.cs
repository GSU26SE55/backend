using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;

namespace AuthService.UnitTests.Validators;

/// <summary>
/// Các luật vừa được siết trong <c>AccountFieldPolicy</c> để BE khớp với ràng buộc FE đang áp:
/// full name tối thiểu 2 ký tự, phone đúng định dạng di động VN, năm sinh không sớm hơn 1900.
///
/// <para>Trước đây mỗi command tự chép lại luật nên ba chỗ này lệch nhau — FE chặn còn BE cho qua.
/// Test giữ cho chúng không trôi lại.</para>
/// </summary>
public class AccountFieldPolicyValidationTests
{
    private static CreateAccountCommand ValidCreate() => new()
    {
        Email = "u@example.com",
        Password = "Strong1Pass!",
        FullName = "Nguyen Van A",
        PhoneNumber = "0912345678",
        DateOfBirth = new DateTime(1990, 1, 1),
        Address = "1 Nguyen Hue",
        RoleId = Guid.NewGuid()
    };

    [Fact]
    public async Task ValidCreate_Passes()
        => (await ValidCreate().ValidateAsync()).IsSuccess.Should().BeTrue();

    /// <summary>FE chặn tên 1 ký tự — BE phải chặn cùng, nếu không gọi thẳng API sẽ lọt.</summary>
    [Fact]
    public async Task FullNameTooShort_Fails()
    {
        var c = ValidCreate();
        c.FullName = "U";

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task FullNameExactly2_Passes()
    {
        var c = ValidCreate();
        c.FullName = "An";

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "FullName");
    }

    /// <summary>Đúng độ dài nhưng sai đầu số / sai định dạng đều phải rớt.</summary>
    [Theory]
    [InlineData("0123456789")]   // đầu số 1 không hợp lệ
    [InlineData("0900111")]      // thiếu số
    [InlineData("09001112223")]  // thừa số
    [InlineData("+84912345678")] // định dạng quốc tế
    [InlineData("091234567a")]   // có chữ
    public async Task BadPhoneNumber_Fails(string phone)
    {
        var c = ValidCreate();
        c.PhoneNumber = phone;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "PhoneNumber");
    }

    [Theory]
    [InlineData("0312345678")]
    [InlineData("0512345678")]
    [InlineData("0712345678")]
    [InlineData("0812345678")]
    [InlineData("0912345678")]
    public async Task GoodPhoneNumber_Passes(string phone)
    {
        var c = ValidCreate();
        c.PhoneNumber = phone;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "PhoneNumber");
    }

    /// <summary>Phone vẫn là optional — bỏ trống không phải lỗi.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyPhoneNumber_Passes(string? phone)
    {
        var c = ValidCreate();
        c.PhoneNumber = phone;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "PhoneNumber");
    }

    [Fact]
    public async Task BirthYearBefore1900_Fails()
    {
        var c = ValidCreate();
        c.DateOfBirth = new DateTime(1899, 12, 31);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    [Fact]
    public async Task FutureDateOfBirth_Fails()
    {
        var c = ValidCreate();
        c.DateOfBirth = DateTime.UtcNow.AddDays(1);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    /// <summary>Cùng bộ luật phải áp cho Register, không riêng gì màn admin tạo account.</summary>
    [Fact]
    public async Task Register_AppliesTheSameRules()
    {
        var c = new RegisterCommand
        {
            Email = "alice@example.com",
            Password = "Strong1Pass!",
            FullName = "A",
            PhoneNumber = "0900111",
            DateOfBirth = new DateTime(1899, 1, 1),
            Address = "Hanoi"
        };

        var errors = (await c.ValidateAsync()).ListErrors;

        errors.Should().Contain(e => e.Field == "FullName");
        errors.Should().Contain(e => e.Field == "PhoneNumber");
        errors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    /// <summary>Invite không có Password nhưng vẫn phải dùng chung luật tên và phone.</summary>
    [Fact]
    public async Task Invite_AppliesTheSameRules()
    {
        var c = new InviteAccountCommand
        {
            Email = "bob@example.com",
            FullName = "B",
            PhoneNumber = "0123456789",
            RoleId = Guid.NewGuid()
        };

        var errors = (await c.ValidateAsync()).ListErrors;

        errors.Should().Contain(e => e.Field == "FullName");
        errors.Should().Contain(e => e.Field == "PhoneNumber");
    }
}
