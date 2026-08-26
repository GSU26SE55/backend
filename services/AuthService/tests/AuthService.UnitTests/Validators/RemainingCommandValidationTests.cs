using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Admin;
using AuthService.Application.CQRS.Command.Auth;

namespace AuthService.UnitTests.Validators;

/// <summary>
/// Phủ nốt các command của AuthService còn để trống ở tầng <c>ValidateAsync</c>.
///
/// <para>Những command này nhận dữ liệu từ client (mã xác thực, id thiết bị, hồ sơ nhân sự) nên luật
/// validate của chúng là hàng rào đầu tiên; trước bộ test này chưa dòng nào được chạy.</para>
/// </summary>
public class UpdateStaffProfileCommandValidationTests
{
    private static UpdateStaffProfileCommand Valid() => new()
    {
        AccountId = Guid.NewGuid(),
        EmployeeCode = "EMP-001",
        Department = "Field Service",
        MaxConcurrentTickets = 5,
        SkillTier = 2,
        Notes = "Senior technician"
    };

    [Fact]
    public async Task Valid_Passes()
    {
        (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();
    }

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

    /// <summary>SkillTier chỉ nhận 1..3 — ngoài khoảng là lỗi.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public async Task SkillTierOutOfRange_Fails(int tier)
    {
        var c = Valid();
        c.SkillTier = tier;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "SkillTier");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task SkillTierAtBoundary_Passes(int tier)
    {
        var c = Valid();
        c.SkillTier = tier;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "SkillTier");
    }

    /// <summary>MaxConcurrentTickets nằm trong 1..50.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task MaxConcurrentTicketsOutOfRange_Fails(int max)
    {
        var c = Valid();
        c.MaxConcurrentTickets = max;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "MaxConcurrentTickets");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public async Task MaxConcurrentTicketsAtBoundary_Passes(int max)
    {
        var c = Valid();
        c.MaxConcurrentTickets = max;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "MaxConcurrentTickets");
    }

    [Fact]
    public async Task EmployeeCodeTooLong_Fails()
    {
        var c = Valid();
        c.EmployeeCode = new string('E', 51);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "EmployeeCode");
    }

    [Fact]
    public async Task DepartmentTooLong_Fails()
    {
        var c = Valid();
        c.Department = new string('D', 101);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Department");
    }

    [Fact]
    public async Task NotesTooLong_Fails()
    {
        var c = Valid();
        c.Notes = new string('n', 1001);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Notes");
    }

    /// <summary>Các trường tuỳ chọn bỏ trống thì không bị kiểm độ dài.</summary>
    [Fact]
    public async Task OptionalFieldsNull_Passes()
    {
        var c = Valid();
        c.EmployeeCode = null;
        c.Department = null;
        c.Notes = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}

public class MergeAccountCommandValidationTests
{
    private static MergeAccountCommand Valid() => new()
    {
        PrimaryAccountId = Guid.NewGuid(),
        SecondaryAccountId = Guid.NewGuid(),
        Reason = "Duplicate registration",
        PerformedBy = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var c = Valid();
        c.PrimaryAccountId = Guid.Empty;
        c.SecondaryAccountId = Guid.Empty;
        var r = await c.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "PrimaryAccountId");
        r.ListErrors.Should().Contain(e => e.Field == "SecondaryAccountId");
    }

    /// <summary>Gộp một tài khoản vào chính nó là vô nghĩa và phải bị chặn.</summary>
    [Fact]
    public async Task MergeIntoItself_Fails()
    {
        var id = Guid.NewGuid();
        var c = Valid();
        c.PrimaryAccountId = id;
        c.SecondaryAccountId = id;

        var r = await c.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "SecondaryAccountId"
            && e.Detail.Contains("into itself"));
    }

    /// <summary>Lý do gộp là bắt buộc vì phục vụ audit.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingReason_Fails(string reason)
    {
        var c = Valid();
        c.Reason = reason;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Reason");
    }

    [Fact]
    public async Task ReasonTooLong_Fails()
    {
        var c = Valid();
        c.Reason = new string('r', 1001);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Reason");
    }

    [Fact]
    public async Task MissingPerformedBy_Fails()
    {
        var c = Valid();
        c.PerformedBy = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "PerformedBy");
    }
}

public class ConfirmCrossDevice2FACommandValidationTests
{
    private static ConfirmCrossDevice2FACommand Valid() => new()
    {
        ConfirmToken = new string('a', 64),   // 64 ký tự hex
        TotpCode = "123456",
        AccountId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingConfirmToken_Fails(string token)
    {
        var c = Valid();
        c.ConfirmToken = token;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ConfirmToken");
    }

    /// <summary>ConfirmToken phải đúng 64 ký tự VÀ toàn hex.</summary>
    [Theory]
    [InlineData("abc")]                          // quá ngắn
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // 64 ký tự nhưng không phải hex
    public async Task InvalidConfirmToken_Fails(string token)
    {
        var c = Valid();
        c.ConfirmToken = token;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ConfirmToken");
    }

    /// <summary>TotpCode phải đúng 6 chữ số.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    public async Task InvalidTotpCode_Fails(string code)
    {
        var c = Valid();
        c.TotpCode = code;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TotpCode");
    }

    [Fact]
    public async Task EmptyAccountId_Fails()
    {
        var c = Valid();
        c.AccountId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "AccountId");
    }
}

public class TrustedDeviceCommandValidationTests
{
    [Fact]
    public async Task RevokeTrustedDevice_Valid_Passes()
    {
        var r = await new RevokeTrustedDeviceCommand
        {
            AccountId = Guid.NewGuid(),
            TrustedDeviceId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeTrustedDevice_EmptyIds_Fail()
    {
        var r = await new RevokeTrustedDeviceCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "AccountId");
        r.ListErrors.Should().Contain(e => e.Field == "TrustedDeviceId");
    }

    [Fact]
    public async Task RevokeAllTrustedDevices_Valid_Passes()
    {
        var r = await new RevokeAllTrustedDevicesCommand { AccountId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAllTrustedDevices_EmptyAccountId_Fails()
    {
        var r = await new RevokeAllTrustedDevicesCommand().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "AccountId");
    }

    [Fact]
    public async Task RequestCrossDevice2FAConfirm_Valid_Passes()
    {
        var r = await new RequestCrossDevice2FAConfirmCommand { AccountId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequestCrossDevice2FAConfirm_EmptyAccountId_Fails()
    {
        var r = await new RequestCrossDevice2FAConfirmCommand().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "AccountId");
    }
}
