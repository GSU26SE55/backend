using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class TechnicalTermMaskerTests
{
    private static TechnicalTermMasker Create() => new();

    // --- Mask ---

    [Fact]
    public void Mask_EmptyString_ReturnsSameAndEmptyMap()
    {
        var (masked, map) = Create().Mask(string.Empty);

        masked.Should().BeEmpty();
        map.Should().BeEmpty();
    }

    [Fact]
    public void Mask_NoTermsPresent_ReturnsSameAndEmptyMap()
    {
        var text = "Pin bị lỗi, cần kiểm tra ngay";

        var (masked, map) = Create().Mask(text);

        masked.Should().Be(text);
        map.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SOH is 80%", "SOH")]
    [InlineData("SLA breach", "SLA")]
    [InlineData("P1 critical ticket", "P1")]
    [InlineData("P2 high priority", "P2")]
    [InlineData("P3 standard", "P3")]
    [InlineData("BMS module", "BMS")]
    [InlineData("MPPT controller", "MPPT")]
    [InlineData("100 kWh battery", "kWh")]
    [InlineData("10 kWp solar", "kWp")]
    [InlineData("DC circuit", "DC")]
    [InlineData("AC output", "AC")]
    [InlineData("Li-ion cell", "Li-ion")]
    [InlineData("ITIL process", "ITIL")]
    public void Mask_KnownTerm_ReplacesWithPlaceholder(string text, string term)
    {
        var (masked, map) = Create().Mask(text);

        masked.Should().NotContain(term);
        map.Should().ContainValue(term);
        map.Should().HaveCount(1);
        masked.Should().MatchRegex(@"\[\[term_\d+\]\]");
    }

    [Fact]
    public void Mask_MultipleTerms_EachGetsUniqueCounter()
    {
        var (masked, map) = Create().Mask("SOH is 80%, SLA breach on P1 ticket");

        map.Should().HaveCount(3);
        map.Keys.Should().Contain("[[term_1]]");
        map.Keys.Should().Contain("[[term_2]]");
        map.Keys.Should().Contain("[[term_3]]");
        map.Values.Should().Contain("SOH");
        map.Values.Should().Contain("SLA");
        map.Values.Should().Contain("P1");
    }

    [Fact]
    public void Mask_SameTermMultipleTimes_EachOccurrenceGetsOwnPlaceholder()
    {
        var (masked, map) = Create().Mask("SOH is 80%, SOH should be above 75%");

        map.Should().HaveCount(2);
        masked.Should().Contain("[[term_1]]");
        masked.Should().Contain("[[term_2]]");
        masked.Should().NotContain("SOH");
    }

    [Fact]
    public void Mask_PartialMatch_NotReplaced()
    {
        // "VSOH123" — SOH embedded in a larger token should not be replaced
        var text = "VSOH123 is a model number";

        var (masked, map) = Create().Mask(text);

        masked.Should().Be(text);
        map.Should().BeEmpty();
    }

    // --- Unmask ---

    [Fact]
    public void Unmask_EmptyTermMap_ReturnsSameText()
    {
        var text = "[[term_1]] is 80%";

        var result = Create().Unmask(text, new Dictionary<string, string>());

        result.Should().Be(text);
    }

    [Fact]
    public void Unmask_RestoredCorrectly()
    {
        var map = new Dictionary<string, string> { ["[[term_1]]"] = "SOH" };

        var result = Create().Unmask("[[term_1]] là 80%", map);

        result.Should().Be("SOH là 80%");
    }

    // --- Roundtrip ---

    [Theory]
    [InlineData("SOH is 80%, SLA breach on P1 ticket with BMS alert")]
    [InlineData("Li-ion cell detected MPPT fault, kWh reading off")]
    [InlineData("No technical terms here")]
    public void Roundtrip_MaskThenUnmask_ReturnsOriginal(string original)
    {
        var masker = Create();
        var (masked, map) = masker.Mask(original);
        var restored = masker.Unmask(masked, map);

        restored.Should().Be(original);
    }
}
