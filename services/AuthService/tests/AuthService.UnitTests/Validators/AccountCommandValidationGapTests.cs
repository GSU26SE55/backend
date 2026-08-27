using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Query.Account;

namespace AuthService.UnitTests.Validators;

/// <summary>
/// Đóng nốt các lớp <c>IValidatable</c> của AuthService chưa có test ở tầng <c>ValidateAsync</c>.
///
/// <para>Nhóm này nhận dữ liệu hồ sơ, lời mời và quyền — nếu luật không chạy thì dữ liệu bẩn đi
/// thẳng xuống handler và xuống DB.</para>
/// </summary>
public class UpdateMyProfileCommandValidationTests
{
    private static UpdateMyProfileCommand Valid() => new()
    {
        AccountId = Guid.NewGuid(),
        FullName = "Nguyen Van A",
        PhoneNumber = "0900000000",
        Address = "12 Nguyen Trai, Ha Noi",
        BirthDate = new DateTime(1995, 5, 20),
        TimeZone = "Asia/Ho_Chi_Minh"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyAccountId_Fails()
    {
        var c = Valid();
        c.AccountId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "AccountId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingFullName_Fails(string name)
    {
        var c = Valid();
        c.FullName = name;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task FullNameTooLong_Fails()
    {
        var c = Valid();
        c.FullName = new string('a', 151);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    /// <summary>Biên trên hợp lệ là đúng 150 ký tự.</summary>
    [Fact]
    public async Task FullNameExactly150_Passes()
    {
        var c = Valid();
        c.FullName = new string('a', 150);

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task PhoneNumberTooLong_Fails()
    {
        var c = Valid();
        c.PhoneNumber = new string('9', 21);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "PhoneNumber");
    }

    [Fact]
    public async Task AddressTooLong_Fails()
    {
        var c = Valid();
        c.Address = new string('x', 501);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Address");
    }

    /// <summary>Ngày sinh trong tương lai là vô lý.</summary>
    [Fact]
    public async Task FutureBirthDate_Fails()
    {
        var c = Valid();
        c.BirthDate = DateTime.UtcNow.AddDays(1);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "BirthDate"
            && e.Detail.Contains("date of birth"));
    }

    /// <summary>Năm sinh trước 1900 gần như chắc chắn là gõ nhầm.</summary>
    [Fact]
    public async Task BirthYearBefore1900_Fails()
    {
        var c = Valid();
        c.BirthDate = new DateTime(1899, 12, 31);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "BirthDate"
            && e.Detail.Contains("birth year"));
    }

    [Fact]
    public async Task TimeZoneTooLong_Fails()
    {
        var c = Valid();
        c.TimeZone = new string('z', 101);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TimeZone");
    }

    /// <summary>Mọi trường tuỳ chọn bỏ trống đều hợp lệ.</summary>
    [Fact]
    public async Task OptionalFieldsNull_Passes()
    {
        var c = Valid();
        c.PhoneNumber = null;
        c.Address = null;
        c.BirthDate = null;
        c.TimeZone = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}

public class InviteAccountCommandValidationTests
{
    private static InviteAccountCommand Valid() => new()
    {
        Email = "staff@example.com",
        FullName = "Tran Thi B",
        PhoneNumber = "0911222333",
        RoleId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingEmail_Fails(string email)
    {
        var c = Valid();
        c.Email = email;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Email"
            && e.Detail.Contains("required"));
    }

    [Fact]
    public async Task EmailTooLong_Fails()
    {
        var c = Valid();
        c.Email = new string('a', 250) + "@ex.com";   // 257 ký tự

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Email"
            && e.Detail.Contains("256"));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    [InlineData("@example.com")]
    public async Task InvalidEmailFormat_Fails(string email)
    {
        var c = Valid();
        c.Email = email;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Email"
            && e.Detail.Contains("format"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task MissingFullName_Fails(string name)
    {
        var c = Valid();
        c.FullName = name;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task FullNameTooLong_Fails()
    {
        var c = Valid();
        c.FullName = new string('n', 151);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task PhoneNumberTooLong_Fails()
    {
        var c = Valid();
        c.PhoneNumber = new string('0', 21);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "PhoneNumber");
    }

    /// <summary>Mỗi account chỉ có một role, nên role là bắt buộc ngay từ lúc mời.</summary>
    [Fact]
    public async Task EmptyRoleId_Fails()
    {
        var c = Valid();
        c.RoleId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "RoleId");
    }
}

public class AddStaffSkillCommandValidationTests
{
    private static AddStaffSkillCommand Valid() => new()
    {
        StaffAccountId = Guid.NewGuid(),
        SkillCode = "BATTERY_SWAP",
        SkillLevel = 3,
        CertifiedUntil = DateTime.UtcNow.AddYears(1)
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyStaffAccountId_Fails()
    {
        var c = Valid();
        c.StaffAccountId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "StaffAccountId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingSkillCode_Fails(string code)
    {
        var c = Valid();
        c.SkillCode = code;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "SkillCode");
    }

    [Fact]
    public async Task SkillCodeTooLong_Fails()
    {
        var c = Valid();
        c.SkillCode = new string('S', 65);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "SkillCode");
    }

    /// <summary>SkillLevel chỉ nhận 1..5.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task SkillLevelOutOfRange_Fails(int level)
    {
        var c = Valid();
        c.SkillLevel = level;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "SkillLevel");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task SkillLevelAtBoundary_Passes(int level)
    {
        var c = Valid();
        c.SkillLevel = level;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "SkillLevel");
    }
}

public class DeleteStaffSkillCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new DeleteStaffSkillCommand
        {
            StaffAccountId = Guid.NewGuid(),
            SkillCode = "BATTERY_SWAP"
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyFields_Fail()
    {
        var r = await new DeleteStaffSkillCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "StaffAccountId");
        r.ListErrors.Should().Contain(e => e.Field == "SkillCode");
    }
}

public class SetMyAvatarCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new SetMyAvatarCommand
        {
            AccountId = Guid.NewGuid(),
            AvatarFileId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var r = await new SetMyAvatarCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "AccountId");
        r.ListErrors.Should().Contain(e => e.Field == "AvatarFileId");
    }
}

public class AcceptInviteCommandValidationTests
{
    private static AcceptInviteCommand Valid() => new()
    {
        InvitationToken = "invitation-token-value",
        Password = "Str0ng!Passw0rd",
        ConfirmPassword = "Str0ng!Passw0rd"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingToken_Fails(string token)
    {
        var c = Valid();
        c.InvitationToken = token;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "InvitationToken");
    }

    /// <summary>Mật khẩu yếu bị chặn theo <c>PasswordPolicy</c> dùng chung.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("alllowercase123")]
    public async Task WeakPassword_Fails(string password)
    {
        var c = Valid();
        c.Password = password;
        c.ConfirmPassword = password;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Password");
    }

    /// <summary>
    /// Lệch xác nhận mật khẩu là lỗi cross-field nên trả 422, khác với 400 của lỗi từng trường.
    /// </summary>
    [Fact]
    public async Task ConfirmPasswordMismatch_Returns422()
    {
        var c = Valid();
        c.ConfirmPassword = "Different!Pass1";

        var r = await c.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(422);
        r.ListErrors.Should().Contain(e => e.Field == "ConfirmPassword");
    }

    /// <summary>Chỉ lỗi từng trường (không lệch xác nhận) thì vẫn là 400.</summary>
    [Fact]
    public async Task FieldOnlyError_Returns400()
    {
        var c = Valid();
        c.InvitationToken = "";

        var r = await c.ValidateAsync();

        r.StatusCode.Should().Be(400);
    }
}

public class SetRolePermissionsCommandValidationTests
{
    [Fact]
    public async Task ValidPermissionIds_Passes()
    {
        var r = await new SetRolePermissionsCommand
        {
            RoleId = Guid.NewGuid(),
            PermissionIds = [Guid.NewGuid(), Guid.NewGuid()]
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Danh sách rỗng hợp lệ — nghĩa là gỡ toàn bộ quyền của role.</summary>
    [Fact]
    public async Task EmptyList_Passes()
    {
        var r = await new SetRolePermissionsCommand { RoleId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ListContainingEmptyGuid_Fails()
    {
        var r = await new SetRolePermissionsCommand
        {
            RoleId = Guid.NewGuid(),
            PermissionIds = [Guid.NewGuid(), Guid.Empty]
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "PermissionIds");
    }
}

public class GetStaffAssignmentProfileQueryValidationTests
{
    [Fact]
    public async Task ValidId_Passes()
    {
        var r = await new GetStaffAssignmentProfileQuery { StaffAccountId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyId_Fails()
    {
        var r = await new GetStaffAssignmentProfileQuery().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "StaffAccountId");
    }
}
