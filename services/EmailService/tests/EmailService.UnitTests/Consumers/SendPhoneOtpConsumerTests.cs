using EmailService.Infrastructure.Consumers;
using MassTransit;

namespace EmailService.UnitTests.Consumers;

public class SendPhoneOtpConsumerTests
{
    [Fact]
    public void SendPhoneOtpConsumer_IsExcludedFromConfigureEndpoints()
    {
        typeof(SendPhoneOtpConsumer)
            .GetCustomAttributes(typeof(ExcludeFromConfigureEndpointsAttribute), inherit: false)
            .Should()
            .NotBeEmpty();
    }
}
