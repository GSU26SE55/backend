using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Channels;

public class SmsBusChannelTests
{
    private static (Mock<IPublishEndpoint> pub, SmsBusChannel channel) Build()
    {
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendSmsCommand>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        var channel = new SmsBusChannel(pub.Object, NullLogger<SmsBusChannel>.Instance);
        return (pub, channel);
    }

    [Fact]
    public async Task SendAsync_ValidPhoneNumber_PublishesSmsCommandAndReturnsSuccess()
    {
        var (pub, channel) = Build();
        var notificationId = Guid.NewGuid();
        var request = new SendRequest
        {
            NotificationId = notificationId,
            UserId = Guid.NewGuid(),
            Title = "Alert",
            Body = "Your battery has an issue",
            PhoneNumber = "0901234567"
        };

        var result = await channel.SendAsync(request);

        result.Success.Should().BeTrue();
        pub.Verify(p => p.Publish(
            It.Is<SendSmsCommand>(cmd =>
                cmd.PhoneNumber == "0901234567" &&
                cmd.Message == "Your battery has an issue" &&
                cmd.SourceService == "notification" &&
                cmd.CorrelationId == notificationId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyPhoneNumber_ReturnsFailureWithoutPublish(string? phone)
    {
        var (pub, channel) = Build();
        var request = new SendRequest { NotificationId = Guid.NewGuid(), PhoneNumber = phone };

        var result = await channel.SendAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No phone number");
        pub.Verify(p => p.Publish(It.IsAny<SendSmsCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_PublishThrows_ReturnsFailure()
    {
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendSmsCommand>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new Exception("RabbitMQ down"));
        var channel = new SmsBusChannel(pub.Object, NullLogger<SmsBusChannel>.Instance);

        var result = await channel.SendAsync(new SendRequest { NotificationId = Guid.NewGuid(), PhoneNumber = "0901234567", Body = "body" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RabbitMQ down");
    }

    [Fact]
    public void ChannelType_IsSms()
    {
        var (_, channel) = Build();
        channel.ChannelType.Should().Be(NotificationChannelEnum.Sms);
    }
}
