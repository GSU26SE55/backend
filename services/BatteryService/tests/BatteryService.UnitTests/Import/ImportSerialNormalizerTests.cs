using BatteryService.Application.Import;

namespace BatteryService.UnitTests.Import;

/// <summary>I3 — chuẩn hoá mã seri.</summary>
public class ImportSerialNormalizerTests
{
    [Theory]
    [InlineData("pyl/us3000c 88a21", "PYL-US3000C-88A21")]
    [InlineData("  PYL-US3000C-88A21  ", "PYL-US3000C-88A21")]
    [InlineData("abc__def", "ABC-DEF")]
    [InlineData("abc///def", "ABC-DEF")]
    [InlineData("trailing---", "TRAILING")]
    [InlineData("///leading", "LEADING")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_ProducesUppercaseAlphanumericWithSingleHyphens(string input, string expected)
    {
        ImportSerialNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_ResultAlwaysMatchesTheSerialConstraint()
    {
        // Ràng buộc của hệ thống là chỉ chữ in hoa, chữ số và gạch nối. Bộ chuẩn hoá phải bảo đảm
        // điều đó cho mọi đầu vào, nếu không thì lỗi chỉ lộ ra lúc ghi xuống cơ sở dữ liệu.
        var inputs = new[] { "a b c", "!!!", "Ω-123", "x/y\\z", "1.2.3", "----", "n" };

        foreach (var input in inputs)
        {
            var normalized = ImportSerialNormalizer.Normalize(input);
            normalized.Should().MatchRegex("^[A-Z0-9-]*$", $"input \"{input}\" must normalize cleanly");
            normalized.Should().NotContain("--");
            normalized.Should().NotEndWith("-");
        }
    }

    [Theory]
    [InlineData("kh_001.a", "KH_001.A")]
    [InlineData("kh 001", "KH-001")]
    public void NormalizeReference_KeepsUnderscoreAndDot(string input, string expected)
    {
        // Mã tham chiếu là mã nội bộ của đối tác — chỉ cần một dạng ổn định để tra, không cần ép
        // về khuôn mã seri của mình.
        ImportSerialNormalizer.NormalizeReference(input).Should().Be(expected);
    }
}
