using EmailService.Infrastructure.Consumers;
using EmailService.IntegrationTests.Fixtures;
using MassTransit;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// Phone OTP ownership đã được tách sang SmsService. EmailService giữ stub legacy để compile/test cũ
/// không vỡ, nhưng không được tự tạo MassTransit endpoint cho SendPhoneOtpEvent nữa.
/// </summary>
[Collection("EmailServiceIntegration")]
public class SendPhoneOtpConsumerIntegrationTests
{
    private readonly EmailServiceFactory _factory;

    public SendPhoneOtpConsumerIntegrationTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void SendPhoneOtpConsumer_IsExcludedFromEmailServiceEndpoints()
    {
        _ = _factory.CreateClient();

        typeof(SendPhoneOtpConsumer)
            .GetCustomAttributes(typeof(ExcludeFromConfigureEndpointsAttribute), inherit: false)
            .Should()
            .NotBeEmpty();
    }
}
