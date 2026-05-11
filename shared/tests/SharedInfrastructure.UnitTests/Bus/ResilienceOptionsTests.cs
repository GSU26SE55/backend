using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.Bus;

/// <summary>
/// Verify ResilienceOptions default values + binding từ Resilience section.
/// AddMessageBus đã register IOptions&lt;ResilienceOptions&gt; — test này check binding hoạt động.
/// </summary>
public class ResilienceOptionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> overrides) =>
        new ConfigurationBuilder().AddInMemoryCollection(overrides).Build();

    [Fact]
    public void Defaults_MatchSpec_FiveRetriesExponentialOneToThirty()
    {
        var options = new ResilienceOptions();

        options.MessageRetry.Limit.Should().Be(5);
        options.MessageRetry.MinIntervalSeconds.Should().Be(1);
        options.MessageRetry.MaxIntervalSeconds.Should().Be(30);
        options.MessageRetry.IntervalDeltaSeconds.Should().Be(2);
        options.ScheduledRedelivery.Enabled.Should().BeTrue();
        options.ScheduledRedelivery.IntervalsMinutes.Should().Be("5,15,30");
        options.ScheduledRedelivery.GetIntervalsMinutes().Should().BeEquivalentTo(new[] { 5, 15, 30 });
    }

    [Fact]
    public void Binding_FromConfig_OverridesAllFields()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost",
            ["RabbitMQ:Username"] = "guest",
            ["RabbitMQ:Password"] = "guest",
            ["Resilience:MessageRetry:Limit"] = "10",
            ["Resilience:MessageRetry:MinIntervalSeconds"] = "2",
            ["Resilience:MessageRetry:MaxIntervalSeconds"] = "60",
            ["Resilience:MessageRetry:IntervalDeltaSeconds"] = "5",
            ["Resilience:ScheduledRedelivery:Enabled"] = "false",
            ["Resilience:ScheduledRedelivery:IntervalsMinutes"] = "10,60"
        });

        var services = new ServiceCollection();
        services.AddMessageBus(cfg);
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        resolved.MessageRetry.Limit.Should().Be(10);
        resolved.MessageRetry.MinIntervalSeconds.Should().Be(2);
        resolved.MessageRetry.MaxIntervalSeconds.Should().Be(60);
        resolved.MessageRetry.IntervalDeltaSeconds.Should().Be(5);
        resolved.ScheduledRedelivery.Enabled.Should().BeFalse();
        resolved.ScheduledRedelivery.IntervalsMinutes.Should().Be("10,60");
        resolved.ScheduledRedelivery.GetIntervalsMinutes().Should().BeEquivalentTo(new[] { 10, 60 });
    }

    [Theory]
    [InlineData("", new int[0])]
    [InlineData("   ", new int[0])]
    [InlineData("5", new[] { 5 })]
    [InlineData("5,10,15", new[] { 5, 10, 15 })]
    [InlineData(" 5 , 10 , 15 ", new[] { 5, 10, 15 })]      // trim whitespace
    [InlineData("5,abc,10", new[] { 5, 10 })]                 // skip invalid
    [InlineData("5,-3,10,0", new[] { 5, 10 })]                // skip negative + zero
    public void GetIntervalsMinutes_ParseCsv_HandlesEdgeCases(string csv, int[] expected)
    {
        var opt = new ScheduledRedeliveryOptions { IntervalsMinutes = csv };
        opt.GetIntervalsMinutes().Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Binding_OnlyPartialOverride_KeepsDefaultsForRest()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "localhost",
            ["RabbitMQ:Username"] = "guest",
            ["RabbitMQ:Password"] = "guest",
            ["Resilience:MessageRetry:Limit"] = "3"
            // Các field khác KHÔNG set → giữ default
        });

        var services = new ServiceCollection();
        services.AddMessageBus(cfg);
        var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;

        resolved.MessageRetry.Limit.Should().Be(3); // overridden
        resolved.MessageRetry.MinIntervalSeconds.Should().Be(1); // default
        resolved.MessageRetry.MaxIntervalSeconds.Should().Be(30); // default
        resolved.ScheduledRedelivery.Enabled.Should().BeTrue(); // default
    }
}
