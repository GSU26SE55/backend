using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Session;
using AuthService.Domain.Enums;

namespace AuthService.UnitTests.Validators;

public class CreateAccountCommandValidationTests
{
    private static CreateAccountCommand Valid() => new()
    {
        Email = "u@e.com",
        Password = "Strong1Pass!",
        FullName = "User",
        PhoneNumber = "0900111222",
        DateOfBirth = new DateTime(1990, 1, 1),
        Address = "Addr",
        RoleId = Guid.NewGuid()
    };

    [Fact] public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    public async Task BadEmail_Fails(string email)
    {
        var c = Valid();
        c.Email = email;
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Email");
    }

    [Fact]
    public async Task Password_Empty_Fails()
    {
        var c = Valid();
        c.Password = "";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Password");
    }

    [Fact]
    public async Task Password_TooShort_Fails()
    {
        var c = Valid();
        c.Password = "abc12";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Password");
    }

    [Theory]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoDigit!Pass")]
    [InlineData("NoSpecial1Pass")]
    public async Task Password_NotStrong_Fails(string password)
    {
        var c = Valid();
        c.Password = password;
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Password");
    }

    [Fact]
    public async Task FullName_Empty_Fails()
    {
        var c = Valid();
        c.FullName = "";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task DateOfBirth_Future_Fails()
    {
        var c = Valid();
        c.DateOfBirth = DateTime.UtcNow.AddDays(1);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    [Fact]
    public async Task RoleId_Empty_Fails()
    {
        var c = Valid();
        c.RoleId = Guid.Empty;
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "RoleId");
    }
}

public class UpdateAccountCommandValidationTests
{
    private static UpdateAccountCommand Valid() => new()
    {
        Id = Guid.NewGuid(),
        FullName = "User",
        PhoneNumber = "0900111222",
        AvatarUrl = null,
        DateOfBirth = new DateTime(1990, 1, 1),
        Address = "Addr"
    };

    [Fact] public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails()
    {
        var c = Valid();
        c.Id = Guid.Empty;
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Fact]
    public async Task EmptyFullName_Fails()
    {
        var c = Valid();
        c.FullName = "";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task FullName_TooLong_Fails()
    {
        var c = Valid();
        c.FullName = new string('a', 151);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "FullName");
    }

    [Fact]
    public async Task DateOfBirth_Future_Fails()
    {
        var c = Valid();
        c.DateOfBirth = DateTime.UtcNow.AddDays(1);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    [Fact]
    public async Task Address_TooLong_Fails()
    {
        var c = Valid();
        c.Address = new string('a', 501);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Address");
    }
}

public class ChangePasswordCommandValidationTests
{
    private static ChangePasswordCommand Valid() => new()
    {
        AccountId = Guid.NewGuid(),
        CurrentPassword = "old123",
        NewPassword = "NewPass123!",
        ConfirmPassword = "NewPass123!"
    };

    [Fact] public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyAccountId_Fails()
    {
        var c = Valid();
        c.AccountId = Guid.Empty;
        (await c.ValidateAsync()).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyCurrentPassword_Fails()
    {
        var c = Valid();
        c.CurrentPassword = "";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "CurrentPassword");
    }

    [Fact]
    public async Task NewPassword_TooShort_Fails()
    {
        var c = Valid();
        c.NewPassword = "abc12";
        c.ConfirmPassword = "abc12";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NewPassword");
    }

    [Fact]
    public async Task NewPassword_NotStrong_Fails()
    {
        var c = Valid();
        c.NewPassword = "NoSpecial123";
        c.ConfirmPassword = "NoSpecial123";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NewPassword");
    }

    [Fact]
    public async Task ConfirmMismatch_Fails()
    {
        var c = Valid();
        c.ConfirmPassword = "different";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ConfirmPassword");
    }

    [Fact]
    public async Task NewSameAsCurrent_Fails()
    {
        var c = Valid();
        c.CurrentPassword = "samepw";
        c.NewPassword = "samepw";
        c.ConfirmPassword = "samepw";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NewPassword");
    }
}

public class ChangeAccountStatusCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new ChangeAccountStatusCommand { Id = Guid.NewGuid(), Status = AccountStatusEnum.Active }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails() =>
        (await new ChangeAccountStatusCommand { Id = Guid.Empty, Status = AccountStatusEnum.Active }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task UndefinedStatus_Fails() =>
        (await new ChangeAccountStatusCommand { Id = Guid.NewGuid(), Status = (AccountStatusEnum)999 }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Reason_TooLong_Fails()
    {
        var r = await new ChangeAccountStatusCommand { Id = Guid.NewGuid(), Status = AccountStatusEnum.Banned, Reason = new string('a', 501) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Reason");
    }
}

public class ChangeEmailCommandValidationTests
{
    private static ChangeEmailCommand Valid() => new()
    {
        AccountId = Guid.NewGuid(),
        NewEmail = "new@e.com",
        CurrentPassword = "pw"
    };

    [Fact] public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyAccountId_Fails()
    {
        var c = Valid();
        c.AccountId = Guid.Empty;
        (await c.ValidateAsync()).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    public async Task BadEmail_Fails(string email)
    {
        var c = Valid();
        c.NewEmail = email;
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NewEmail");
    }

    [Fact]
    public async Task EmptyPassword_Fails()
    {
        var c = Valid();
        c.CurrentPassword = "";
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "CurrentPassword");
    }
}

public class ConfirmEmailChangeCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new ConfirmEmailChangeCommand { AccountId = Guid.NewGuid(), Otp = "123456" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails() =>
        (await new ConfirmEmailChangeCommand { AccountId = Guid.Empty, Otp = "123456" }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("12345a")]
    public async Task BadOtp_Fails(string otp) =>
        (await new ConfirmEmailChangeCommand { AccountId = Guid.NewGuid(), Otp = otp }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class ChangeAccountRoleCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new ChangeAccountRoleCommand { AccountId = Guid.NewGuid(), RoleId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyAccountId_Fails() =>
        (await new ChangeAccountRoleCommand { AccountId = Guid.Empty, RoleId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task EmptyRoleId_Fails() =>
        (await new ChangeAccountRoleCommand { AccountId = Guid.NewGuid(), RoleId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class SimpleAccountCommandValidationTests
{
    [Fact]
    public async Task Unlock_Valid_Passes() =>
        (await new UnlockAccountCommand { Id = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Unlock_Empty_Fails() =>
        (await new UnlockAccountCommand { Id = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Deactivate_Valid_Passes() =>
        (await new DeactivateMeCommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Deactivate_Empty_Fails() =>
        (await new DeactivateMeCommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task DeleteMe_Valid_Passes() =>
        (await new DeleteMeCommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task DeleteMe_Empty_Fails() =>
        (await new DeleteMeCommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class AdminRevokeAccountSessionsCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new AdminRevokeAccountSessionsCommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Empty_Fails() =>
        (await new AdminRevokeAccountSessionsCommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}
