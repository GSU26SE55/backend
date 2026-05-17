using AuthService.Application.CQRS.Command.Auth;
using AuthService.Domain.Enums;

namespace AuthService.UnitTests.Validators;

public class RegisterCommandValidationTests
{
    private static RegisterCommand Valid() => new()
    {
        Email = "alice@example.com",
        Password = "Strong1Pass!",
        FullName = "Alice",
        PhoneNumber = "0900111",
        DateOfBirth = new DateTime(1990, 1, 1),
        Address = "Hanoi"
    };

    [Fact] public async Task Valid_AllRulesPass() { (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue(); }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@nope.com")]
    [InlineData("a@b")]
    public async Task InvalidEmail_Fails(string email)
    {
        var c = Valid();
        c.Email = email;
        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Email");
    }

    [Fact]
    public async Task Email_TooLong_Fails()
    {
        var c = Valid();
        c.Email = new string('a', 260) + "@x.com";
        (await c.ValidateAsync()).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]                 // empty
    [InlineData("short1!")]          // < 8
    [InlineData("alllowercase1!")]   // missing upper
    [InlineData("ALLUPPERCASE1!")]   // missing lower
    [InlineData("NoDigit!Pass")]     // missing digit
    [InlineData("NoSpecial1Pass")]   // missing special
    public async Task WeakPassword_Fails(string pw)
    {
        var c = Valid();
        c.Password = pw;
        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Password");
    }

    [Fact]
    public async Task Password_TooLong_Fails()
    {
        var c = Valid();
        c.Password = new string('A', 90) + "1aaaaa!aaaaaaaa";
        (await c.ValidateAsync()).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FullName_Empty_Fails()
    {
        var c = Valid();
        c.FullName = "  ";
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
    public async Task DateOfBirth_Before1900_Fails()
    {
        var c = Valid();
        c.DateOfBirth = new DateTime(1899, 12, 31);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "DateOfBirth");
    }

    [Fact]
    public async Task PhoneNumber_TooLong_Fails()
    {
        var c = Valid();
        c.PhoneNumber = new string('0', 21);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "PhoneNumber");
    }

    [Fact]
    public async Task Address_TooLong_Fails()
    {
        var c = Valid();
        c.Address = new string('a', 501);
        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Address");
    }
}

public class LoginCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new LoginCommand { Email = "u@e.com", Password = "abc123" }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Email")]
    [InlineData("not-email", "Email")]
    public async Task InvalidEmail_Fails(string email, string field)
    {
        var r = await new LoginCommand { Email = email, Password = "abc123" }.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == field);
    }

    [Fact]
    public async Task Password_Empty_Fails()
    {
        var r = await new LoginCommand { Email = "u@e.com", Password = "" }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Password");
    }

    [Theory]
    [InlineData("abc12")]
    [InlineData("abc 123")]
    public async Task Password_NonEmptyOnly_Passes(string password)
    {
        var r = await new LoginCommand { Email = "u@e.com", Password = password }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }
}

public class VerifyOtpCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new VerifyOtpCommand { Email = "u@e.com", Otp = "123456" }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345a")]
    [InlineData("1234567")]
    [InlineData("")]
    public async Task InvalidOtp_Fails(string otp)
    {
        var r = await new VerifyOtpCommand { Email = "u@e.com", Otp = otp }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Otp");
    }

    [Fact]
    public async Task InvalidEmail_Fails()
    {
        var r = await new VerifyOtpCommand { Email = "bad", Otp = "123456" }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Email");
    }
}

public class ResetPasswordCommandValidationTests
{
    [Fact]
    public async Task Valid_StrongPassword_Passes()
    {
        var r = await new ResetPasswordCommand { ResetToken = "tok", NewPassword = "Strong1Pass!" }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("alllower1!")]
    [InlineData("ALLUPPER1!")]
    [InlineData("NoDigit!Pa")]
    [InlineData("NoSpecial1A")]
    [InlineData("Sh1!")]
    public async Task WeakPassword_Fails(string pw)
    {
        var r = await new ResetPasswordCommand { ResetToken = "t", NewPassword = pw }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "NewPassword");
    }

    [Fact]
    public async Task ResetToken_Empty_Fails()
    {
        var r = await new ResetPasswordCommand { ResetToken = "", NewPassword = "Strong1Pass!" }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "ResetToken");
    }
}

public class VerifyResetOtpCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new VerifyResetOtpCommand { Email = "u@e.com", Otp = "654321" }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public async Task BadOtp_Fails(string otp)
    {
        var r = await new VerifyResetOtpCommand { Email = "u@e.com", Otp = otp }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Otp");
    }
}

public class ForgotPasswordCommandValidationTests
{
    [Fact]
    public async Task ValidEmail_Passes() =>
        (await new ForgotPasswordCommand { Email = "u@e.com" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    public async Task BadEmail_Fails(string email) =>
        (await new ForgotPasswordCommand { Email = email }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class ResendOtpCommandValidationTests
{
    [Fact]
    public async Task ValidEmail_Passes() =>
        (await new ResendOtpCommand { Email = "u@e.com" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task BadEmail_Fails() =>
        (await new ResendOtpCommand { Email = "bad" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class ResendResetOtpCommandValidationTests
{
    [Fact]
    public async Task ValidEmail_Passes() =>
        (await new ResendResetOtpCommand { Email = "u@e.com" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task BadEmail_Fails() =>
        (await new ResendResetOtpCommand { Email = "bad" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class RefreshTokenCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new RefreshTokenCommand { RefreshToken = "tok" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Empty_Fails() =>
        (await new RefreshTokenCommand { RefreshToken = "" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class LogoutCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new LogoutCommand { AccountId = Guid.NewGuid(), RefreshToken = "tok" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Empty_Fails() =>
        (await new LogoutCommand { AccountId = Guid.NewGuid(), RefreshToken = "" }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task MissingAccountId_Fails401()
    {
        var result = await new LogoutCommand { RefreshToken = "tok" }.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}

public class GoogleAuthCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new GoogleAuthCommand { IdToken = "tok" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Empty_Fails() =>
        (await new GoogleAuthCommand { IdToken = "" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class GoogleCallbackCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new GoogleCallbackCommand { Code = "c", RedirectUri = "https://x" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyCode_Fails()
    {
        var r = await new GoogleCallbackCommand { Code = "", RedirectUri = "https://x" }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Code");
    }

    [Fact]
    public async Task EmptyRedirectUri_Fails()
    {
        var r = await new GoogleCallbackCommand { Code = "c", RedirectUri = "" }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "RedirectUri");
    }
}

public class SendPhoneOtpCommandValidationTests
{
    [Fact]
    public async Task ValidId_Passes() =>
        (await new SendPhoneOtpCommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyId_Fails() =>
        (await new SendPhoneOtpCommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class VerifyPhoneOtpCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes() =>
        (await new VerifyPhoneOtpCommand { AccountId = Guid.NewGuid(), Otp = "123456" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task BadOtp_Fails() =>
        (await new VerifyPhoneOtpCommand { AccountId = Guid.NewGuid(), Otp = "abc" }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class TwoFactorCommandValidationTests
{
    [Fact]
    public async Task Enable_Valid_Passes() =>
        (await new Enable2FACommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Enable_Empty_Fails() =>
        (await new Enable2FACommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Disable_Valid_Passes() =>
        (await new Disable2FACommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Disable_Empty_Fails() =>
        (await new Disable2FACommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}

public class LinkUnlinkGoogleCommandValidationTests
{
    [Fact]
    public async Task Link_Valid_Passes() =>
        (await new LinkGoogleCommand { AccountId = Guid.NewGuid(), IdToken = "t" }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Link_EmptyAccountId_Fails() =>
        (await new LinkGoogleCommand { AccountId = Guid.Empty, IdToken = "t" }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Link_EmptyIdToken_Fails() =>
        (await new LinkGoogleCommand { AccountId = Guid.NewGuid(), IdToken = "" }.ValidateAsync()).IsSuccess.Should().BeFalse();

    [Fact]
    public async Task Unlink_Valid_Passes() =>
        (await new UnlinkGoogleCommand { AccountId = Guid.NewGuid() }.ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Unlink_Empty_Fails() =>
        (await new UnlinkGoogleCommand { AccountId = Guid.Empty }.ValidateAsync()).IsSuccess.Should().BeFalse();
}
