using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Implements.Helpers;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Test JwtHelper: generate/validate access token + reset token. Dùng key thật, không mock JWT lib.
/// </summary>
public class JwtHelperTests
{
    private const string SecretKey = "super-secret-test-key-must-be-at-least-32-chars-long-12345";
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private readonly JwtHelper _sut;

    public JwtHelperTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = SecretKey,
                ["JwtSettings:Issuer"] = Issuer,
                ["JwtSettings:Audience"] = Audience
            })
            .Build();
        _sut = new JwtHelper(config);
    }

    private static Account MakeAccount(Guid? id = null, string? email = "user@test.com", string? fullName = "User Test")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Email = email!,
            FullName = fullName!,
            PasswordHash = "h",
            Status = AuthService.Domain.Enums.AccountStatusEnum.Active
        };

    private static IEnumerable<Claim> ParseClaims(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ReadJwtToken(token).Claims;
    }

    [Fact]
    public async Task GenerateAccessToken_IncludesStandardClaims_WithSingleRole()
    {
        // 1-N refactor: role là single string trong JWT.
        var account = MakeAccount(email: "alice@example.com", fullName: "Alice");
        var token = await _sut.GenerateAccessToken(account, "Customer");

        var claims = ParseClaims(token).ToList();

        claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        claims.Should().Contain(c => c.Type == "nameid" && c.Value == account.Id.ToString());
        claims.Should().Contain(c => c.Type == "AccountId" && c.Value == account.Id.ToString());
        claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "alice@example.com");
        claims.Should().Contain(c => c.Type == "FullName" && c.Value == "Alice");
        claims.Where(c => c.Type == "role").Select(c => c.Value)
              .Should().BeEquivalentTo(new[] { "Customer" });
    }

    [Fact]
    public async Task GenerateAccessToken_NullEmailAndFullName_UsesEmptyString()
    {
        var account = MakeAccount(email: null!, fullName: null);
        var token = await _sut.GenerateAccessToken(account, string.Empty);

        var claims = ParseClaims(token).ToList();
        claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == string.Empty);
        claims.Should().Contain(c => c.Type == "FullName" && c.Value == string.Empty);
    }

    [Fact]
    public async Task GenerateAccessToken_NullRole_DoesNotEmitRoleClaim()
    {
        var account = MakeAccount();

        var token = await _sut.GenerateAccessToken(account, null!);

        token.Should().NotBeNullOrEmpty();
        ParseClaims(token).Should().NotContain(c => c.Type == "role");
    }

    [Fact]
    public async Task GenerateAccessToken_WhitespaceRole_IsFilteredOut()
    {
        var account = MakeAccount();

        var token = await _sut.GenerateAccessToken(account, "   ");

        ParseClaims(token).Should().NotContain(c => c.Type == "role");
    }

    [Fact]
    public async Task GenerateAccessToken_HasIssuerAudienceAnd1HourExpiry()
    {
        var token = await _sut.GenerateAccessToken(MakeAccount(), string.Empty);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(Audience);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateRefreshToken_Returns32CharHex_NoHyphens()
    {
        var t1 = _sut.GenerateRefreshToken();
        var t2 = _sut.GenerateRefreshToken();

        t1.Should().HaveLength(32);
        t1.Should().NotContain("-");
        t1.Should().NotBe(t2, "mỗi lần phải sinh khác nhau");
    }

    [Fact]
    public async Task ValidateToken_ValidGeneratedToken_ReturnsTrue()
    {
        var token = await _sut.GenerateAccessToken(MakeAccount(), "Customer");

        var (ok, err) = _sut.ValidateToken(token);

        ok.Should().BeTrue();
        err.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_GarbageToken_Throws()
    {
        // ValidateToken không catch — caller phải catch. Đây là behavior hiện tại của implementation.
        var act = () => _sut.ValidateToken("not-a-jwt");
        act.Should().Throw<Exception>();
    }

    [Fact]
    public async Task ValidateToken_TokenSignedWithOtherKey_Throws()
    {
        var otherConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "different-key-must-be-at-least-32-chars-long-1234567890",
                ["JwtSettings:Issuer"] = Issuer,
                ["JwtSettings:Audience"] = Audience
            })
            .Build();
        var other = new JwtHelper(otherConfig);
        var token = await other.GenerateAccessToken(MakeAccount(), string.Empty);

        var act = () => _sut.ValidateToken(token);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ConvertUnixTimeToDateTime_KnownValue_MatchesExpected()
    {
        // 2020-01-01T00:00:00Z = 1577836800
        var result = _sut.ConvertUnixTimeToDateTime(1577836800);
        result.ToUniversalTime().Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void IsTokenValid_ThrowsNotImplemented()
    {
        var act = () => _sut.IsTokenValid("anything");
        act.Should().Throw<NotImplementedException>();
    }

    // ============== Reset token ==============

    [Fact]
    public void GenerateResetToken_ContainsRequiredClaims()
    {
        var aid = Guid.NewGuid();
        var token = _sut.GenerateResetToken(aid, "user@x.com", expiresInMinutes: 5);

        var claims = ParseClaims(token).ToList();
        claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
        claims.Should().Contain(c => c.Type == "AccountId" && c.Value == aid.ToString());
        claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "user@x.com");
        claims.Should().Contain(c => c.Type == "purpose" && c.Value == "password-reset");
    }

    [Fact]
    public void ValidateResetToken_FreshlyGenerated_ReturnsAccountId()
    {
        var aid = Guid.NewGuid();
        var token = _sut.GenerateResetToken(aid, "u@x.com", 5);

        var (id, err) = _sut.ValidateResetToken(token);

        id.Should().Be(aid);
        err.Should().BeNull();
    }

    [Fact]
    public void ValidateResetToken_EmptyToken_ReturnsError()
    {
        var (id, err) = _sut.ValidateResetToken(" ");

        id.Should().BeNull();
        err.Should().Contain("không được để trống");
    }

    [Fact]
    public async Task ValidateResetToken_WrongPurpose_ReturnsError()
    {
        // Generate access token (not reset token) — không có purpose claim.
        var access = await _sut.GenerateAccessToken(MakeAccount(), string.Empty);

        var (id, err) = _sut.ValidateResetToken(access);

        id.Should().BeNull();
        err.Should().Contain("không phải dành cho reset password");
    }

    [Fact]
    public async Task ValidateResetToken_ExpiredToken_ReturnsExpiredError()
    {
        // GenerateResetToken không cho expiresInMinutes <= 0 (JWT lib reject Expires < NotBefore),
        // nên dùng helper bypass để build token đã quá hạn → test validation expiry path.
        var expired = await TestJwtBuilder.BuildExpiredResetTokenAsync(SecretKey, Issuer, Audience);

        var (id, err) = _sut.ValidateResetToken(expired);

        id.Should().BeNull();
        err.Should().Contain("hết hạn");
    }

    [Fact]
    public void ValidateResetToken_GarbageToken_ReturnsInvalidError()
    {
        var (id, err) = _sut.ValidateResetToken("garbage-not-a-jwt");

        id.Should().BeNull();
        err.Should().Contain("không hợp lệ");
    }

    [Fact]
    public void ValidateResetToken_TokenSignedWithOtherKey_ReturnsInvalidError()
    {
        var otherConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "different-key-must-be-at-least-32-chars-long-1234567890",
                ["JwtSettings:Issuer"] = Issuer,
                ["JwtSettings:Audience"] = Audience
            })
            .Build();
        var other = new JwtHelper(otherConfig);
        var foreign = other.GenerateResetToken(Guid.NewGuid(), "u@x.com", 5);

        var (id, err) = _sut.ValidateResetToken(foreign);

        id.Should().BeNull();
        err.Should().Contain("không hợp lệ");
    }
}
