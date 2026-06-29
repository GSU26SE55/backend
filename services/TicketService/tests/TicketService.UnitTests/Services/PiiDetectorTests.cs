using Moq;
using SharedContracts.Interfaces;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class PiiDetectorTests
{
    private static PiiDetector CreateDetector() =>
        new(new Mock<ICacheService>().Object);

    [Fact]
    public void ContainsPii_Cccd12Digits_ReturnsTrue()
    {
        var detector = CreateDetector();

        var result = detector.ContainsPii("CCCD của tôi là 079201001234", out var matched);

        result.Should().BeTrue();
        matched.Should().Contain("CCCD");
    }

    [Fact]
    public void ContainsPii_VietnamesePhone_ReturnsTrue()
    {
        var detector = CreateDetector();

        var result = detector.ContainsPii("Gọi tôi qua số 0912345678 nhé", out var matched);

        result.Should().BeTrue();
        matched.Should().Contain("SĐT");
    }

    [Fact]
    public void ContainsPii_Email_ReturnsTrue()
    {
        var detector = CreateDetector();

        var result = detector.ContainsPii("Liên hệ qua email a.b@example.com", out var matched);

        result.Should().BeTrue();
        matched.Should().Contain("Email");
    }

    [Fact]
    public void ContainsPii_CleanText_ReturnsFalse()
    {
        var detector = CreateDetector();

        var result = detector.ContainsPii("Cảm ơn bạn đã phản hồi", out var matched);

        result.Should().BeFalse();
        matched.Should().BeEmpty();
    }

    [Fact]
    public void ContainsPii_EmptyBody_ReturnsFalse()
    {
        var detector = CreateDetector();

        var result = detector.ContainsPii("", out var matched);

        result.Should().BeFalse();
        matched.Should().BeEmpty();
    }
}
