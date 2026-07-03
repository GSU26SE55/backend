using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class LinguaLanguageDetectionServiceTests
{
    private readonly LinguaLanguageDetectionService _sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_EmptyOrWhitespace_ReturnsUnd(string? text)
    {
        _sut.Detect(text!).Should().Be("und");
    }

    [Fact]
    public void Detect_EnglishText_ReturnsEn()
    {
        var result = _sut.Detect("The battery state of health has degraded significantly.");
        result.Should().Be("en");
    }

    [Fact]
    public void Detect_VietnameseText_ReturnsVi()
    {
        var result = _sut.Detect("Pin đang ở trạng thái suy giảm, cần bảo trì ngay.");
        result.Should().Be("vi");
    }

    [Fact]
    public void Detect_AmbiguousShortText_ReturnsUndOrKnownLang()
    {
        // Single word — Lingua may return "und"; result must be one of the 3 valid values
        var result = _sut.Detect("ok");
        result.Should().BeOneOf("en", "vi", "und");
    }
}
