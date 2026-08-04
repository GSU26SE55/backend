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

    [Theory]
    [InlineData("ok")]
    [InlineData("nguyên")]
    [InlineData("hello")]
    [InlineData("cảnh báo")]
    public void Detect_ShortText_ReturnsUnd(string text)
    {
        // Under MinWordCount (4) — always "und" regardless of language
        _sut.Detect(text).Should().Be("und");
    }

    [Fact]
    public void Detect_NonEnViText_ReturnsEnOrUnd()
    {
        // Lingua only loads EN + VI — Latin-based languages (French, Spanish...) may be
        // classified as "en" since it's the closest model. The key protection is MinWordCount
        // for short texts; non-EN/VI long text may leak through as "en" which is acceptable.
        var result = _sut.Detect("Le système de gestion de batterie détecte une anomalie.");
        result.Should().BeOneOf("en", "und");
    }
}
