using SmsService.Application.Common;

namespace SmsService.UnitTests.Common;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("0901234567", "+84901234567")]
    [InlineData("0 901 234 567", "+84901234567")]
    [InlineData("(090) 123-4567", "+84901234567")]
    [InlineData("090.123.4567", "+84901234567")]
    [InlineData("84901234567", "+84901234567")]
    [InlineData("+84901234567", "+84901234567")]
    public void NormalizeVn_ValidNumber_ReturnsE164(string raw, string expected)
    {
        var result = PhoneNumberNormalizer.NormalizeVn(raw);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("123")]                     // quá ngắn
    [InlineData("12345678901234567")]       // quá dài
    [InlineData("abcdefghij")]              // ký tự
    [InlineData("0123abc456")]              // mix
    public void NormalizeVn_InvalidNumber_ReturnsNull(string? raw)
    {
        var result = PhoneNumberNormalizer.NormalizeVn(raw!);
        result.Should().BeNull();
    }

    [Fact]
    public void NormalizeVn_LandlinePrefix_ReturnsE164()
    {
        // 0xx vẫn được coi là VN, chỉ cần đủ 10 số bắt đầu 0
        PhoneNumberNormalizer.NormalizeVn("0281234567").Should().Be("+84281234567");
    }
}
