using AuthService.Infrastructure.Implements.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AuthService.UnitTests.Infrastructure.TwoFactor;

public class TwoFactorSecretProtectorTests
{
    private readonly TwoFactorSecretProtector _protector;

    public TwoFactorSecretProtectorTests()
    {
        var provider = new EphemeralDataProtectionProvider();
        _protector = new TwoFactorSecretProtector(provider);
    }

    [Fact]
    public void Protect_PrependsVersionPrefix()
    {
        var protectedValue = _protector.Protect("SECRET123");
        protectedValue.Should().StartWith("enc:v1:");
    }

    [Fact]
    public void Roundtrip_PlaintextSurvivesProtectUnprotect()
    {
        var original = "BASE32SECRETHERE";
        var protectedValue = _protector.Protect(original);
        var recovered = _protector.Unprotect(protectedValue);
        recovered.Should().Be(original);
    }

    [Fact]
    public void Unprotect_LegacyPlaintext_ReturnsAsIs()
    {
        // String không có prefix enc: được coi là plaintext legacy
        var recovered = _protector.Unprotect("RAWBASE32SECRET");
        recovered.Should().Be("RAWBASE32SECRET");
    }

    [Fact]
    public void IsProtected_OnlyPrefixedValues_ReturnTrue()
    {
        _protector.IsProtected("enc:v1:abc").Should().BeTrue();
        _protector.IsProtected("plaintext").Should().BeFalse();
        _protector.IsProtected("").Should().BeFalse();
        _protector.IsProtected("enc:v2:abc").Should().BeFalse(); // v2 not supported yet
    }
}
