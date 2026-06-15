using AuthService.Infrastructure.Implements.Services;
using OtpNet;

namespace AuthService.UnitTests.Infrastructure.TwoFactor;

public class TotpServiceTests
{
    private readonly TotpService _svc = new();

    [Fact]
    public void GenerateSecret_ReturnsValidBase32_32CharsForA20ByteKey()
    {
        var s = _svc.GenerateSecret();
        s.Should().NotBeNullOrEmpty();
        // 20 bytes base32-encoded = 32 chars (no padding for clean 20)
        s.Length.Should().Be(32);
        s.All(c => "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".Contains(c)).Should().BeTrue();
    }

    [Fact]
    public void BuildOtpAuthUri_ContainsRequiredFields()
    {
        var uri = _svc.BuildOtpAuthUri("JBSWY3DPEHPK3PXP", "u@example.com", "TestApp");
        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=JBSWY3DPEHPK3PXP");
        uri.Should().Contain("issuer=TestApp");
        uri.Should().Contain("algorithm=SHA1");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }

    [Fact]
    public void VerifyCode_GeneratedCodeFromSameSecret_ReturnsTrue()
    {
        var secret = _svc.GenerateSecret();
        var bytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(bytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        var code = totp.ComputeTotp();

        _svc.VerifyCode(secret, code).Should().BeTrue();
    }

    [Fact]
    public void VerifyCode_WrongFormat_ReturnsFalse()
    {
        var secret = _svc.GenerateSecret();
        _svc.VerifyCode(secret, "abc").Should().BeFalse();
        _svc.VerifyCode(secret, "12345").Should().BeFalse();  // not 6 digits
        _svc.VerifyCode(secret, "1234567").Should().BeFalse(); // too many
        _svc.VerifyCode(secret, "12345a").Should().BeFalse();  // non-digit
    }

    [Fact]
    public void VerifyCode_RandomCode_AlmostAlwaysReturnsFalse()
    {
        var secret = _svc.GenerateSecret();
        _svc.VerifyCode(secret, "000000").Should().BeFalse();
    }

    [Fact]
    public void VerifyCode_InvalidSecret_ReturnsFalse()
    {
        _svc.VerifyCode("not-base32!", "123456").Should().BeFalse();
    }
}
