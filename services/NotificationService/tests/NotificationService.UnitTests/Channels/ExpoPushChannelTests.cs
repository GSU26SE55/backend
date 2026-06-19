using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.UnitTests.Helpers;
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;

namespace NotificationService.UnitTests.Channels;

public class ExpoPushChannelTests
{
    private const string ExpoToken = "ExponentPushToken[abc123]";

    private static SendRequest MakeRequest(string? token = ExpoToken, bool isCritical = false) => new()
    {
        NotificationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Title = "Alert",
        Body = "Battery anomaly detected",
        ExpoToken = token,
        IsCritical = isCritical
    };

    private static IHttpClientFactory BuildFactory(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("expo")).Returns(client);
        return factory.Object;
    }

    private static HttpResponseMessage ExpoSuccess() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { data = new { status = "ok", id = "ticket-123" } }),
                Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage ExpoDeviceNotRegistered() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        status = "error",
                        message = "\"ExponentPushToken[abc123]\" is not a registered push notification recipient",
                        details = new { error = "DeviceNotRegistered" }
                    }
                }),
                Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task SendAsync_Success_ReturnsTrue()
    {
        var factory = BuildFactory(ExpoSuccess());
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        var result = await channel.SendAsync(MakeRequest());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_SendsCorrectPayload_WithCriticalPriority()
    {
        string? captured = null;
        var handler = new MockHttpMessageHandler(ExpoSuccess(), req =>
        {
            captured = req.Content!.ReadAsStringAsync().Result;
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("expo")).Returns(client);

        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory.Object, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        await channel.SendAsync(MakeRequest(isCritical: true));

        captured.Should().NotBeNull();
        using var doc = JsonDocument.Parse(captured!);
        doc.RootElement.GetProperty("to").GetString().Should().Be(ExpoToken);
        doc.RootElement.GetProperty("priority").GetString().Should().Be("high");
        doc.RootElement.GetProperty("channelId").GetString().Should().Be("alerts-critical");
    }

    [Fact]
    public async Task SendAsync_DeviceNotRegistered_DeactivatesTokenAndReturnsFalse()
    {
        var deviceToken = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = ExpoToken,
            IsActive = true
        };

        var factory = BuildFactory(ExpoDeviceNotRegistered());
        var (uow, deviceTokenRepo, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: [deviceToken]);

        var channel = new ExpoPushChannel(factory, uow.Object, NullLogger<ExpoPushChannel>.Instance);
        var result = await channel.SendAsync(MakeRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("DeviceNotRegistered");
        deviceToken.IsActive.Should().BeFalse();
        deviceTokenRepo.Verify(r => r.UpdateAsync(deviceToken), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_EmptyToken_ReturnsFailureWithoutHttpCall(string? token)
    {
        var factory = new Mock<IHttpClientFactory>();
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory.Object, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        var request = MakeRequest();
        request.ExpoToken = token;
        var result = await channel.SendAsync(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No Expo token");
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_HttpError_ReturnsFailure()
    {
        var factory = BuildFactory(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        var result = await channel.SendAsync(MakeRequest());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void ChannelType_IsPush()
    {
        var factory = new Mock<IHttpClientFactory>();
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory.Object, uow.Object, NullLogger<ExpoPushChannel>.Instance);
        channel.ChannelType.Should().Be(NotificationChannelEnum.Push);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Action<HttpRequestMessage>? _onRequest;

        public MockHttpMessageHandler(HttpResponseMessage response, Action<HttpRequestMessage>? onRequest = null)
        {
            _response = response;
            _onRequest = onRequest;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _onRequest?.Invoke(request);
            return Task.FromResult(_response);
        }
    }
}
