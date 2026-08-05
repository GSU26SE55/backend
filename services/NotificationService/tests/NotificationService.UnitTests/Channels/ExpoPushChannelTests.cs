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
                JsonSerializer.Serialize(new { data = new[] { new { status = "ok", id = "ticket-123" } } }),
                Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage ExpoDeviceNotRegistered() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = new[]
                    {
                        new
                        {
                            status = "error",
                            message = "\"ExponentPushToken[abc123]\" is not a registered push notification recipient",
                            details = new { error = "DeviceNotRegistered" }
                        }
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

        // Sprint 6.2 NOTI-16 (#687) — payload nay là MẢNG message (Expo batch API, tối đa 100/call).
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);

        var message = doc.RootElement[0];
        message.GetProperty("to").GetString().Should().Be(ExpoToken);
        message.GetProperty("priority").GetString().Should().Be("high");
        message.GetProperty("channelId").GetString().Should().Be("alerts-critical");
    }

    /// <summary>
    /// 03/08/2026 — push phải mang theo cặp <c>entityType</c>/<c>entityId</c>.
    ///
    /// <para><b>Vì sao:</b> trước đó <c>data</c> chỉ có <c>notificationId</c> cộng các khoá payload,
    /// nên client phải <i>đoán</i> mở màn nào. Mobile đoán bằng <c>ticketId</c> ⇒ thông báo về pin
    /// (1.228/1.285 dòng = 95,6%) bấm vào không đi đâu cả, còn danh sách trong app lại dùng
    /// <c>entityType</c> — cùng một thông báo mà hai đường mở hai màn khác nhau.</para>
    /// </summary>
    [Fact]
    public async Task SendAsync_GuiKemEntityTypeVaEntityId_DeClientMoDungManHinh()
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

        var batteryId = Guid.NewGuid();
        var request = MakeRequest();
        request.EntityType = "Battery";
        request.EntityId = batteryId;

        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(captured!);
        var data = doc.RootElement[0].GetProperty("data");

        data.GetProperty("entityType").GetString().Should().Be("Battery");
        data.GetProperty("entityId").GetString().Should().Be(batteryId.ToString());
    }

    /// <summary>
    /// Payload do consumer viết KHÔNG được ghi đè cặp định tuyến — nếu ghi đè được thì một consumer
    /// vô tình đặt khoá trùng tên sẽ đẩy người dùng sang màn hình sai.
    /// </summary>
    [Fact]
    public async Task SendAsync_PayloadKhongGhiDeDuocEntityType()
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

        var request = MakeRequest();
        request.EntityType = "Battery";
        request.EntityId = Guid.NewGuid();
        request.PayloadJson = """{"entityType":"Ticket","screen":"TicketDetail"}""";

        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(captured!);
        var data = doc.RootElement[0].GetProperty("data");

        data.GetProperty("entityType").GetString().Should().Be("Battery",
            "cặp định tuyến lấy từ bản ghi notification, không phải từ payload consumer tự viết");
        data.GetProperty("screen").GetString().Should().Be("TicketDetail",
            "các khoá payload khác vẫn được gửi kèm như thường");
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

    /// <summary>
    /// Sprint 6.2 NOTI-16 (#687) — nhiều device token của cùng 1 user gộp vào 1 HTTP call.
    /// Trước đây mỗi token là 1 request riêng.
    /// </summary>
    [Fact]
    public async Task SendAsync_MultipleTokens_SendsSingleBatchedRequest()
    {
        var requestCount = 0;
        string? captured = null;
        var handler = new MockHttpMessageHandler(ExpoSuccessBatch(3), req =>
        {
            requestCount++;
            captured = req.Content!.ReadAsStringAsync().Result;
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("expo")).Returns(client);

        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var channel = new ExpoPushChannel(factory.Object, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        var request = MakeRequest();
        request.ExpoTokens = ["ExponentPushToken[a]", "ExponentPushToken[b]", "ExponentPushToken[c]"];

        var result = await channel.SendAsync(request);

        result.Success.Should().BeTrue();
        requestCount.Should().Be(1, "3 token phải gộp vào đúng 1 request");

        using var doc = JsonDocument.Parse(captured!);
        doc.RootElement.GetArrayLength().Should().Be(3);
    }

    /// <summary>Một token hỏng không được kéo cả batch xuống thất bại.</summary>
    [Fact]
    public async Task SendAsync_PartialFailure_ReturnsSuccess_AndDeactivatesOnlyBadToken()
    {
        var goodToken = "ExponentPushToken[good]";
        var badToken = "ExponentPushToken[bad]";

        var bad = new DeviceTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = badToken,
            IsActive = true
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = new object[]
                    {
                        new { status = "ok", id = "t-1" },
                        new { status = "error", details = new { error = "DeviceNotRegistered" } }
                    }
                }),
                Encoding.UTF8, "application/json")
        };

        var factory = BuildFactory(response);
        var (uow, deviceTokenRepo, _) = MockNotificationUnitOfWork.Build(deviceTokenSeed: [bad]);
        var channel = new ExpoPushChannel(factory, uow.Object, NullLogger<ExpoPushChannel>.Instance);

        var request = MakeRequest();
        request.ExpoTokens = [goodToken, badToken];

        var result = await channel.SendAsync(request);

        result.Success.Should().BeTrue("vẫn có thiết bị nhận được thông báo");
        bad.IsActive.Should().BeFalse();
        deviceTokenRepo.Verify(r => r.UpdateAsync(bad), Times.Once);
    }

    private static HttpResponseMessage ExpoSuccessBatch(int count) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = Enumerable.Range(0, count).Select(i => new { status = "ok", id = $"ticket-{i}" }).ToArray()
                }),
                Encoding.UTF8, "application/json")
        };

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
