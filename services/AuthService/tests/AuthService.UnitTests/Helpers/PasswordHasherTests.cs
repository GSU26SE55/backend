using AuthService.Infrastructure.Implements.Helpers;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Test PasswordHasher (BCrypt wrapper, work factor 12). Dùng BCrypt thật, không mock.
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void Hash_ValidPassword_ProducesBcryptHash()
    {
        var hash = _sut.Hash("MyPass123!");

        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2"); // BCrypt prefix
        hash.Length.Should().Be(60);   // BCrypt always 60 chars
    }

    [Fact]
    public void Hash_SamePassword_TwiceProducesDifferentHashes()
    {
        var h1 = _sut.Hash("Same");
        var h2 = _sut.Hash("Same");

        h1.Should().NotBe(h2, "BCrypt salts mỗi lần — hash phải khác nhau");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Hash_EmptyOrWhitespace_ThrowsArgumentException(string? pwd)
    {
        var act = () => _sut.Hash(pwd!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var pwd = "Correct!23";
        var hash = _sut.Hash(pwd);

        _sut.Verify(pwd, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _sut.Hash("Correct!23");

        _sut.Verify("Wrong!23", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "any-hash")]
    [InlineData(null, "any-hash")]
    [InlineData("password", "")]
    [InlineData("password", null)]
    public void Verify_EmptyInput_ReturnsFalse(string? pwd, string? hash)
    {
        _sut.Verify(pwd!, hash!).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse_DoesNotThrow()
    {
        // Hash invalid → BCrypt throws SaltParseException; PasswordHasher catches → false.
        _sut.Verify("password", "not-a-valid-bcrypt-hash").Should().BeFalse();
    }
}
