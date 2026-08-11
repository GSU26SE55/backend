using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// Sprint 6.3 NOTI3-15 (#715) — token cho hủy đăng ký một chạm.
///
/// Endpoint hủy buộc phải mở công khai (Gmail/Yahoo POST tự động, không kèm cookie/JWT), nên toàn bộ
/// khả năng chống lạm dụng nằm ở token: đúng người, đúng nhóm, có hạn dùng, không giả mạo được.
/// </summary>
public class UnsubscribeTokenServiceTests
{
    private static UnsubscribeTokenService Sut(string? secret = "khoa-bi-mat-du-dai-de-ky-hmac", string? baseUrl = null)
    {
        var values = new Dictionary<string, string?>();
        if (secret is not null)
            values["Notification:Unsubscribe:Secret"] = secret;
        if (baseUrl is not null)
            values["Notification:Unsubscribe:PublicBaseUrl"] = baseUrl;

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new UnsubscribeTokenService(config);
    }

    [Fact]
    public void Create_ThenValidate_RoundTrips()
    {
        var sut = Sut();
        var userId = Guid.NewGuid();

        var token = sut.Create(userId, NotificationCategoryEnum.Chat);

        token.Should().NotBeNullOrWhiteSpace();
        sut.TryValidate(token, out var parsedUser, out var parsedCategory).Should().BeTrue();
        parsedUser.Should().Be(userId);
        parsedCategory.Should().Be(NotificationCategoryEnum.Chat);
    }

    /// <summary>
    /// Đây là điều duy nhất ngăn người lạ tắt thông báo của người khác: sửa payload phải làm hỏng
    /// chữ ký.
    /// </summary>
    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var sut = Sut();
        var token = sut.Create(Guid.NewGuid(), NotificationCategoryEnum.Chat)!;

        var parts = token.Split('.');
        var forged = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"{Guid.NewGuid():N}.{(int)NotificationCategoryEnum.Sla}.{DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        sut.TryValidate($"{forged}.{parts[1]}", out _, out _).Should().BeFalse();
    }

    /// <summary>Token ký bằng khoá khác phải bị từ chối — nếu không, khoá ký chỉ là trang trí.</summary>
    [Fact]
    public void TokenFromDifferentSecret_IsRejected()
    {
        var issuer = Sut("khoa-cua-he-thong-that");
        var attacker = Sut("khoa-doan-mo");

        var forged = attacker.Create(Guid.NewGuid(), NotificationCategoryEnum.Sla);

        issuer.TryValidate(forged, out _, out _).Should().BeFalse();
    }

    /// <summary>Email nằm trong hộp thư nhiều năm — token vô hạn là cánh cửa mở mãi.</summary>
    [Fact]
    public void ExpiredToken_IsRejected()
    {
        var sut = Sut();
        var issuedAt = DateTime.UtcNow.AddDays(-400);

        var token = sut.Create(Guid.NewGuid(), NotificationCategoryEnum.Chat, issuedAt);

        sut.TryValidate(token, out _, out _).Should().BeFalse("token mặc định sống 180 ngày");
    }

    [Fact]
    public void TokenStillValid_BeforeExpiry()
    {
        var sut = Sut();
        var issuedAt = DateTime.UtcNow.AddDays(-10);

        var token = sut.Create(Guid.NewGuid(), NotificationCategoryEnum.Chat, issuedAt);

        sut.TryValidate(token, out _, out _).Should().BeTrue();
    }

    /// <summary>
    /// Chưa cấu hình khoá ⇒ KHÔNG phát hành token, thay vì rơi về khoá mặc định (ai đọc mã nguồn
    /// cũng ký được link hợp lệ).
    /// </summary>
    [Fact]
    public void NoSecretConfigured_IssuesNothing_AndValidatesNothing()
    {
        var sut = Sut(secret: null);

        sut.Create(Guid.NewGuid(), NotificationCategoryEnum.Chat).Should().BeNull();
        sut.TryValidate("bat-ky-thu-gi", out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khong-co-dau-cham")]
    [InlineData("qua.nhieu.dau.cham")]
    [InlineData("!!!khong-phai-base64!!!.chu-ky")]
    public void MalformedToken_IsRejectedWithoutThrowing(string? token)
    {
        var sut = Sut();

        var act = () => sut.TryValidate(token, out _, out _);

        act.Should().NotThrow();
        sut.TryValidate(token, out _, out _).Should().BeFalse();
    }

    /// <summary>Token phải an toàn khi nằm trong query string — Base64 chuẩn có <c>+ / =</c> sẽ vỡ.</summary>
    [Fact]
    public void Token_IsUrlSafe()
    {
        var sut = Sut();

        for (var i = 0; i < 30; i++)
        {
            var token = sut.Create(Guid.NewGuid(), NotificationCategoryEnum.Battery)!;
            token.Should().NotContainAny("+", "/", "=");
        }
    }

    /// <summary>Token ràng đúng một nhóm — không được dùng lại để hủy nhóm khác.</summary>
    [Fact]
    public void Token_BindsExactCategory()
    {
        var sut = Sut();
        var userId = Guid.NewGuid();

        var token = sut.Create(userId, NotificationCategoryEnum.Battery)!;

        sut.TryValidate(token, out _, out var category).Should().BeTrue();
        category.Should().Be(NotificationCategoryEnum.Battery);
        category.Should().NotBe(NotificationCategoryEnum.Sla);
    }
}

/// <summary>
/// Sprint 6.3 NOTI3-15 (#715) — email thông báo mang link hủy đúng NHÓM của nó.
/// </summary>
public class EmailBusChannelUnsubscribeTests
{
    private static (EmailBusChannel channel, List<SendNotificationEmailEvent> published) Build(
        string? secret = "khoa-bi-mat", string? baseUrl = "https://api.solarbattery.site")
    {
        var published = new List<SendNotificationEmailEvent>();

        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
               .Callback<SendNotificationEmailEvent, CancellationToken>((e, _) => published.Add(e))
               .Returns(Task.CompletedTask);

        var values = new Dictionary<string, string?>();
        if (secret is not null)
            values["Notification:Unsubscribe:Secret"] = secret;
        if (baseUrl is not null)
            values["Notification:Unsubscribe:PublicBaseUrl"] = baseUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var channel = new EmailBusChannel(
            publish.Object,
            NullLogger<EmailBusChannel>.Instance,
            new UnsubscribeTokenService(config));

        return (channel, published);
    }

    private static SendRequest Request(NotificationTypeEnum type) => new()
    {
        NotificationId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Type = type,
        Title = "Title",
        Body = "Content",
        Email = "user@x.com",
    };

    [Fact]
    public async Task PublishedEvent_CarriesUnsubscribeUrl()
    {
        var (channel, published) = Build();

        await channel.SendAsync(Request(NotificationTypeEnum.ChatCreated));

        published.Should().ContainSingle();
        published[0].UnsubscribeUrl.Should().StartWith("https://api.solarbattery.site/api/notification-unsubscribe?token=");
    }

    /// <summary>
    /// Link phải ràng nhóm của chính notification đó: người dùng hủy vì chat làm phiền
    /// không được mất luôn cảnh báo SLA.
    /// </summary>
    [Fact]
    public async Task UnsubscribeUrl_BindsNotificationCategory()
    {
        var (channel, published) = Build();
        var request = Request(NotificationTypeEnum.ChatCreated);

        await channel.SendAsync(request);

        var token = Uri.UnescapeDataString(published[0].UnsubscribeUrl!.Split("token=")[1]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notification:Unsubscribe:Secret"] = "khoa-bi-mat" })
            .Build();

        new UnsubscribeTokenService(config)
            .TryValidate(token, out var userId, out var category).Should().BeTrue();

        userId.Should().Be(request.UserId);
        category.Should().Be(NotificationCategoryEnum.Chat);
    }

    /// <summary>Thiếu cấu hình thì email vẫn phải gửi được — chỉ mất nút hủy, không hỏng đường gửi.</summary>
    [Theory]
    [InlineData(null, "https://api.solarbattery.site")]
    [InlineData("khoa-bi-mat", null)]
    public async Task MissingConfiguration_StillSendsEmail_WithoutUnsubscribeUrl(string? secret, string? baseUrl)
    {
        var (channel, published) = Build(secret, baseUrl);

        var result = await channel.SendAsync(Request(NotificationTypeEnum.ChatCreated));

        result.Success.Should().BeTrue();
        published.Should().ContainSingle();
        published[0].UnsubscribeUrl.Should().BeNull();
    }
}
