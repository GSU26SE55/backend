using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Domain.Entities;
using NotificationService.Infrastructure.Channels;
using NotificationService.UnitTests.Helpers;

namespace NotificationService.UnitTests.Channels;

/// <summary>
/// Sprint 6.3 NOTI3-02 (#702) — hai guard mà <c>ExpoPushChannel</c> phải giữ:
/// trần payload 4KB của Expo, và lưu ticket id để đối soát về sau.
/// </summary>
public class ExpoPushPayloadLimitTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public string? Body { get; private set; }

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }

    private static HttpResponseMessage Ok(params string[] ticketIds) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { data = ticketIds.Select(id => new { status = "ok", id }).ToArray() }),
                Encoding.UTF8, "application/json")
        };

    private static (ExpoPushChannel channel, CapturingHandler handler) Build(
        Moq.Mock<NotificationService.Application.Interfaces.Repositories.INotificationUnitOfWork> uow,
        HttpResponseMessage response)
    {
        var handler = new CapturingHandler(response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://exp.host") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("expo")).Returns(client);

        return (new ExpoPushChannel(factory.Object, uow.Object, NullLogger<ExpoPushChannel>.Instance), handler);
    }

    private static SendRequest Request(string title, string body, string? payloadJson = null) => new()
    {
        NotificationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Title = title,
        Body = body,
        PayloadJson = payloadJson,
        ExpoToken = "ExponentPushToken[abc]",
    };

    /// <summary>Message bình thường phải đi nguyên vẹn — guard không được cắt oan.</summary>
    [Fact]
    public async Task NormalMessage_IsSentUntouched()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));

        await channel.SendAsync(Request("Cảnh báo pin", "Nhiệt độ vượt ngưỡng 45°C"));

        using var doc = JsonDocument.Parse(handler.Body!);
        var msg = doc.RootElement[0];
        msg.GetProperty("title").GetString().Should().Be("Cảnh báo pin");
        msg.GetProperty("body").GetString().Should().Be("Nhiệt độ vượt ngưỡng 45°C");
    }

    /// <summary>
    /// Payload data khổng lồ → bỏ CONTEXT NGHIỆP VỤ trước, giữ nguyên tiêu đề/nội dung.
    ///
    /// Sprint 6.3 NOTI3-14 (#714) — đổi hành vi: trước đây bỏ SẠCH <c>data</c>. Nay vẫn phải giữ
    /// <c>notificationId</c>, nếu không client mất khả năng gọi <c>PATCH /{id}/opened</c> đúng ở
    /// những push dài nhất — thứ đáng đo open-rate nhất.
    /// </summary>
    [Fact]
    public async Task OversizedData_DropsBusinessContext_ButKeepsNotificationId()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));

        var hugePayload = JsonSerializer.Serialize(new { blob = new string('x', 8000) });
        var request = Request("Cảnh báo", "Pin lỗi", hugePayload);
        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(handler.Body!);
        var msg = doc.RootElement[0];

        msg.GetProperty("title").GetString().Should().Be("Cảnh báo");
        msg.GetProperty("body").GetString().Should().Be("Pin lỗi");

        var data = msg.GetProperty("data");
        data.ValueKind.Should().Be(JsonValueKind.Object);
        data.GetProperty("notificationId").GetGuid().Should().Be(request.NotificationId);
        data.TryGetProperty("blob", out _).Should().BeFalse("context nghiệp vụ mới là thứ bị hy sinh");

        Encoding.UTF8.GetByteCount(handler.Body!).Should().BeLessThan(4096);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — <c>data</c> LUÔN mang <c>notificationId</c>, kèm nguyên context
    /// nghiệp vụ khi message còn vừa trần. Không có id thì mobile không biết gọi
    /// <c>PATCH /api/notifications/{id}/opened</c> cho record nào.
    /// </summary>
    [Fact]
    public async Task Data_AlwaysCarriesNotificationId_AlongsideBusinessPayload()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));

        var ticketId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { ticketId, chatId = "c-1" });
        var request = Request("Tin nhắn mới", "Bạn có bình luận mới", payload);
        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(handler.Body!);
        var data = doc.RootElement[0].GetProperty("data");

        data.GetProperty("notificationId").GetGuid().Should().Be(request.NotificationId);
        data.GetProperty("ticketId").GetGuid().Should().Be(ticketId);
        data.GetProperty("chatId").GetString().Should().Be("c-1");
    }

    [Fact]
    public async Task Data_CarriesDeepLinkAndConversationMetadata()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));
        var entityId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 8, 4, 12, 30, 0, DateTimeKind.Utc);
        var request = Request("Tin nhắn mới", "Nội dung");
        request.Type = NotificationService.Domain.Enums.NotificationTypeEnum.ChatCreated;
        request.EntityType = "Chat";
        request.EntityId = entityId;
        request.CreatedAt = createdAt;

        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(handler.Body!);
        var data = doc.RootElement[0].GetProperty("data");
        data.GetProperty("entityType").GetString().Should().Be("Chat");
        data.GetProperty("entityId").GetGuid().Should().Be(entityId);
        data.GetProperty("createdAt").GetDateTime().Should().Be(createdAt);
        data.GetProperty("notificationType").GetInt32()
            .Should().Be((int)NotificationService.Domain.Enums.NotificationTypeEnum.ChatCreated);
    }

    /// <summary>
    /// Payload do consumer tự viết KHÔNG được ghi đè <c>notificationId</c> — nếu đè được thì client
    /// sẽ đánh dấu "đã mở" nhầm sang record khác. Payload hỏng cũng vẫn phải gửi kèm id.
    /// </summary>
    [Theory]
    [InlineData("{\"notificationId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("khong-phai-json")]
    [InlineData("[1,2,3]")]
    public async Task Payload_CannotOverrideOrRemoveNotificationId(string payloadJson)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));

        var request = Request("Tiêu đề", "Nội dung", payloadJson);
        await channel.SendAsync(request);

        using var doc = JsonDocument.Parse(handler.Body!);
        var data = doc.RootElement[0].GetProperty("data");

        data.GetProperty("notificationId").GetGuid().Should().Be(request.NotificationId);
    }

    /// <summary>Body quá dài (không phải do data) → cắt theo BYTE, không được vỡ ký tự tiếng Việt.</summary>
    [Fact]
    public async Task OversizedBody_IsTruncated_WithoutBreakingUtf8()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var (channel, handler) = Build(uow, Ok("t1"));

        // Chuỗi tiếng Việt có dấu: mỗi ký tự 3 byte UTF-8 ⇒ cắt theo index ký tự sẽ sai trần.
        var longBody = string.Concat(Enumerable.Repeat("đêm", 3000));
        await channel.SendAsync(Request("Cảnh báo", longBody));

        using var doc = JsonDocument.Parse(handler.Body!);
        var sent = doc.RootElement[0].GetProperty("body").GetString()!;

        Encoding.UTF8.GetByteCount(sent).Should().BeLessThanOrEqualTo(4096);
        sent.Should().EndWith("…");
        // Round-trip qua UTF-8 không đổi ⇒ không có ký tự bị cắt vỡ.
        Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(sent)).Should().Be(sent);
    }

    /// <summary>Không lưu ticket id thì không bao giờ đối soát được — cả NOTI3-02 vô nghĩa.</summary>
    [Fact]
    public async Task SuccessfulSend_PersistsPushReceipt()
    {
        var receipts = new List<PushReceipt>();
        var (uow, _, _) = MockNotificationUnitOfWork.Build(pushReceiptSeed: receipts);
        var (channel, _) = Build(uow, Ok("ticket-abc"));

        var request = Request("T", "B");
        await channel.SendAsync(request);

        var stored = uow.Object.PushReceipts.GetAllAsync().ToList();
        stored.Should().ContainSingle();
        stored[0].TicketId.Should().Be("ticket-abc");
        stored[0].NotificationId.Should().Be(request.NotificationId);
        stored[0].DeviceToken.Should().Be("ExponentPushToken[abc]");
        stored[0].Status.Should().Be(PushReceiptStatusEnum.Pending);
    }

    [Fact]
    public async Task MultipleTokens_PersistOneReceiptPerDevice()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(pushReceiptSeed: new List<PushReceipt>());
        var (channel, _) = Build(uow, Ok("t1", "t2", "t3"));

        var request = Request("T", "B");
        request.ExpoTokens = ["ExponentPushToken[a]", "ExponentPushToken[b]", "ExponentPushToken[c]"];

        await channel.SendAsync(request);

        uow.Object.PushReceipts.GetAllAsync().ToList().Should().HaveCount(3);
    }

    /// <summary>Expo không trả id (ticket lỗi) thì không có gì để đối soát — đừng tạo rác.</summary>
    [Fact]
    public async Task ErrorTicket_DoesNotCreateReceipt()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    data = new[] { new { status = "error", details = new { error = "DeviceNotRegistered" } } }
                }),
                Encoding.UTF8, "application/json")
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(pushReceiptSeed: new List<PushReceipt>());
        var (channel, _) = Build(uow, response);

        await channel.SendAsync(Request("T", "B"));

        uow.Object.PushReceipts.GetAllAsync().ToList().Should().BeEmpty();
    }
}
