using AuthService.Infrastructure.Implements.Services;

namespace AuthService.UnitTests.Infrastructure.TwoFactor;

public class BackupCodeGeneratorTests
{
    private readonly BackupCodeGenerator _gen = new();

    [Fact]
    public void Generate_8Codes_AllNonEmpty_AllDistinct()
    {
        var codes = _gen.Generate(8);
        codes.Should().HaveCount(8);
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(c => c.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Generate_Format_HasDashInMiddle()
    {
        var codes = _gen.Generate(5);
        codes.Should().AllSatisfy(c =>
        {
            c.Length.Should().Be(9); // 8 chars + 1 dash
            c[4].Should().Be('-');
        });
    }

    [Fact]
    public void Generate_DoesNotIncludeAmbiguousChars()
    {
        var codes = _gen.Generate(20);
        foreach (var c in codes)
            c.Should().NotContainAny("0", "o", "O", "l", "L", "1", "I", "i");
    }

    [Fact]
    public void Hash_NormalizesBeforeHashing_SoCaseAndDashIgnored()
    {
        var hash = _gen.Hash("ABCD-1234");
        _gen.Verify("abcd-1234", hash).Should().BeTrue();
        _gen.Verify("ABCD1234", hash).Should().BeTrue();
        _gen.Verify("abcd1234", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongCode_ReturnsFalse()
    {
        var hash = _gen.Hash("abcd-1234");
        _gen.Verify("xyzw-9876", hash).Should().BeFalse();
        _gen.Verify("", hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        _gen.Verify("abcd-1234", "not-a-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void Normalize_RemovesDashAndLowercases()
    {
        _gen.Normalize("ABCD-1234").Should().Be("abcd1234");
        _gen.Normalize("  abcd-1234  ").Should().Be("abcd1234");
        _gen.Normalize("").Should().Be("");
    }

    [Fact]
    public void Generate_InvalidCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _gen.Generate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _gen.Generate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _gen.Generate(51));
    }
}
