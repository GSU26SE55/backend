using SmsService.Infrastructure.Security;

namespace SmsService.UnitTests.Security;

public class BcryptGatewayApiKeyHasherTests
{
    private readonly BcryptGatewayApiKeyHasher _sut = new();

    [Fact]
    public void Hash_ProducesNonEmptyString_AndIsBcryptFormat()
    {
        var hash = _sut.Hash("my-api-key");

        hash.Should().NotBeNullOrWhiteSpace();
        // BCrypt format: $2[abxy]$<cost>$<22-char-salt><31-char-hash>
        hash.Should().StartWith("$2");
        hash.Length.Should().BeGreaterThan(50);
    }

    [Fact]
    public void Hash_SameInput_DifferentSalts()
    {
        var h1 = _sut.Hash("identical-key");
        var h2 = _sut.Hash("identical-key");

        // BCrypt randomly salts → 2 hash khác nhau dù plaintext giống.
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Verify_CorrectKey_ReturnsTrue()
    {
        var key = "secret-key-123";
        var hash = _sut.Hash(key);

        _sut.Verify(key, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var hash = _sut.Hash("right-key");

        _sut.Verify("wrong-key", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("not-a-bcrypt-hash")]
    [InlineData("")]
    [InlineData("$2b$invalid-format")]
    public void Verify_MalformedHash_ReturnsFalse_DoesNotThrow(string malformedHash)
    {
        // Spec §68.13.1: try-catch trong Verify để trả 403 thay vì 500 lên client.
        Action act = () => _sut.Verify("any", malformedHash);
        act.Should().NotThrow();

        _sut.Verify("any", malformedHash).Should().BeFalse();
    }

    [Fact]
    public void Hash_WorkFactor_Matches11()
    {
        // BCrypt format: $2[abxy]$<cost>$...
        var hash = _sut.Hash("test");
        var parts = hash.Split('$');

        // parts[0]="", parts[1]="2b" (or 2a), parts[2]="11" (cost)
        parts.Should().HaveCountGreaterThan(2);
        parts[2].Should().Be("11");
    }
}
