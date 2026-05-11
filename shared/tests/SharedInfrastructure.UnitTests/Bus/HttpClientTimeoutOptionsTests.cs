using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.Bus;

public class HttpClientTimeoutOptionsTests
{
    [Fact]
    public void Defaults_MatchExpectedValues()
    {
        var o = new HttpClientTimeoutOptions();

        o.GoogleOAuthSeconds.Should().Be(15);
        o.MailjetSeconds.Should().Be(30);
        o.DefaultSeconds.Should().Be(30);
    }

    [Fact]
    public void TimeSpanProperties_ReflectSeconds()
    {
        var o = new HttpClientTimeoutOptions
        {
            GoogleOAuthSeconds = 10,
            MailjetSeconds = 45,
            DefaultSeconds = 60
        };

        o.GoogleOAuth.Should().Be(TimeSpan.FromSeconds(10));
        o.Mailjet.Should().Be(TimeSpan.FromSeconds(45));
        o.Default.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ZeroOrNegative_FallsBackToSafeDefault(int invalidValue)
    {
        // Nếu config bị bug (set = 0 hoặc âm) → KHÔNG hardcode 0s (sẽ throw RequestTimeout ngay).
        // Phải fallback sang default để service vẫn chạy.
        var o = new HttpClientTimeoutOptions
        {
            GoogleOAuthSeconds = invalidValue,
            MailjetSeconds = invalidValue,
            DefaultSeconds = invalidValue
        };

        o.GoogleOAuth.Should().Be(TimeSpan.FromSeconds(15));
        o.Mailjet.Should().Be(TimeSpan.FromSeconds(30));
        o.Default.Should().Be(TimeSpan.FromSeconds(30));
    }
}
