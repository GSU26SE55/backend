using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class ProfanityFilterTests
{
    private static ProfanityFilter CreateFilter()
    {
        var options = new ChatOptions
        {
            ProfanityWords = new Dictionary<string, List<string>>
            {
                ["VN"] = new() { "ngu", "đồ khốn" },
                ["EN"] = new() { "stupid", "idiot" }
            }
        };
        return new ProfanityFilter(Options.Create(options));
    }

    [Fact]
    public void ContainsProfanity_VietnameseWord_ReturnsTrue()
    {
        var filter = CreateFilter();

        var result = filter.ContainsProfanity("Mày là đồ ngu", out var matched);

        result.Should().BeTrue();
        matched.Should().NotBeEmpty();
    }

    [Fact]
    public void ContainsProfanity_EnglishWordCaseInsensitive_ReturnsTrue()
    {
        var filter = CreateFilter();

        var result = filter.ContainsProfanity("You are so STUPID", out var matched);

        result.Should().BeTrue();
        matched.Should().Contain("stupid");
    }

    [Fact]
    public void ContainsProfanity_CleanText_ReturnsFalse()
    {
        var filter = CreateFilter();

        var result = filter.ContainsProfanity("Cảm ơn bạn đã hỗ trợ", out var matched);

        result.Should().BeFalse();
        matched.Should().BeEmpty();
    }

    [Fact]
    public void ContainsProfanity_EmptyBody_ReturnsFalse()
    {
        var filter = CreateFilter();

        var result = filter.ContainsProfanity("", out var matched);

        result.Should().BeFalse();
        matched.Should().BeEmpty();
    }
}
