using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Channels;

public class EmailBusChannelTests
{
    private static (Mock<IPublishEndpoint> pub, EmailBusChannel channel) Build()
    {
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        var channel = new EmailBusChannel(pub.Object, NullLogger<EmailBusChannel>.Instance);
        return (pub, channel);
    }

    [Fact]
    public async Task SendAsync_ValidEmail_PublishesEventAndReturnsSuccess()
    {
        var (pub, channel) = Build();
        var notificationId = Guid.NewGuid();
        var request = new SendRequest
        {
            NotificationId = notificationId,
            UserId = Guid.NewGuid(),
            Title = "Ticket được giao",
            Body = "Ticket #123 đã được giao cho bạn.",
            Email = "staff@example.com"
        };

        var result = await channel.SendAsync(request);

        result.Success.Should().BeTrue();
        pub.Verify(p => p.Publish(
            It.Is<SendNotificationEmailEvent>(evt =>
                evt.NotificationId == notificationId &&
                evt.ToEmail == "staff@example.com" &&
                evt.Subject == "Ticket được giao" &&
                evt.Body == "Ticket #123 đã được giao cho bạn." &&
                evt.SourceService == "notification"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyEmail_ReturnsFailureWithoutPublish(string? email)
    {
        var (pub, channel) = Build();
        var request = new SendRequest { NotificationId = Guid.NewGuid(), Email = email };

        var result = await channel.SendAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No email address");
        pub.Verify(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_PublishThrows_ReturnsFailure()
    {
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new Exception("broker unavailable"));
        var channel = new EmailBusChannel(pub.Object, NullLogger<EmailBusChannel>.Instance);

        var result = await channel.SendAsync(new SendRequest
        {
            NotificationId = Guid.NewGuid(),
            Email = "user@example.com",
            Title = "title",
            Body = "body"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("broker unavailable");
    }

    [Fact]
    public void ChannelType_IsEmail()
    {
        var (_, channel) = Build();
        channel.ChannelType.Should().Be(NotificationChannelEnum.Email);
    }
}
