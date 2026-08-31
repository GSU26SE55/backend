using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;

namespace AuthService.UnitTests.Validators;

/// <summary>
/// Luật email từng bị chép tay ở 7 command, và 5 trong số đó quên hẳn cap 256 ký tự mà
/// FE luôn áp — gọi thẳng API là đẩy được email dài vô hạn vào tầng dưới. Giờ tất cả dùng
/// chung <c>AccountFieldPolicy.AddEmailErrors</c>.
/// </summary>
public class AuthEmailPolicyTests
{
    private static string TooLong() => new string('a', 250) + "@example.com"; // 262 ký tự

    [Fact]
    public async Task ForgotPassword_RejectsOverlongEmail()
        => (await new ForgotPasswordCommand { Email = TooLong() }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task ResendOtp_RejectsOverlongEmail()
        => (await new ResendOtpCommand { Email = TooLong() }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task ResendResetOtp_RejectsOverlongEmail()
        => (await new ResendResetOtpCommand { Email = TooLong() }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task VerifyOtp_RejectsOverlongEmail()
        => (await new VerifyOtpCommand { Email = TooLong(), Otp = "123456" }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task VerifyResetOtp_RejectsOverlongEmail()
        => (await new VerifyResetOtpCommand { Email = TooLong(), Otp = "123456" }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task Login_RejectsOverlongEmail()
        => (await new LoginCommand { Email = TooLong(), Password = "Strong1Pass!" }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

    [Fact]
    public async Task ChangeEmail_ReportsOnItsOwnFieldName()
    {
        var result = await new ChangeEmailCommand
        {
            AccountId = Guid.NewGuid(),
            NewEmail = TooLong(),
            CurrentPassword = "Strong1Pass!"
        }.ValidateAsync();

        // Field phải là "NewEmail" — FE gắn lỗi xuống ô theo đúng tên này.
        result.ListErrors.Should().Contain(e => e.Field == "NewEmail");
    }

    /// <summary>Hành vi cũ không được vỡ: email hợp lệ vẫn qua, email sai định dạng vẫn rớt.</summary>
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData("@nope.com", false)]
    public async Task ForgotPassword_KeepsExistingBehaviour(string email, bool shouldPass)
    {
        var result = await new ForgotPasswordCommand { Email = email }.ValidateAsync();

        result.ListErrors.Any(e => e.Field == "Email").Should().Be(!shouldPass);
    }

    /// <summary>OTP vẫn được validate độc lập với email.</summary>
    [Fact]
    public async Task VerifyOtp_StillChecksOtpFormat()
        => (await new VerifyOtpCommand { Email = "user@example.com", Otp = "abc" }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Otp");

    /// <summary>Hai command reactivate trước đây không implement IValidatable — không có
    /// luật nào chạy, email rác và OTP "abc" đi thẳng xuống handler.</summary>
    [Fact]
    public async Task ReactivateRequest_ValidatesEmail()
    {
        (await new ReactivateRequestCommand { Email = "not-an-email" }.ValidateAsync())
            .ListErrors.Should().Contain(e => e.Field == "Email");

        (await new ReactivateRequestCommand { Email = "user@example.com" }.ValidateAsync())
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ReactivateVerify_ValidatesEmailAndOtp()
    {
        var bad = await new ReactivateVerifyCommand { Email = "nope", Otp = "abc" }.ValidateAsync();
        bad.ListErrors.Should().Contain(e => e.Field == "Email");
        bad.ListErrors.Should().Contain(e => e.Field == "Otp");

        (await new ReactivateVerifyCommand { Email = "user@example.com", Otp = "123456" }.ValidateAsync())
            .IsSuccess.Should().BeTrue();
    }
}
