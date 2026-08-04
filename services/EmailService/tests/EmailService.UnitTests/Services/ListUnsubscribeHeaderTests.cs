using System.Net;
using System.Text.Json;
using EmailService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace EmailService.UnitTests.Services;

/// <summary>
/// Sprint 6.3 NOTI3-15 (#715) — <c>List-Unsubscribe</c> đi vào payload Mailjet.
///
/// Từ 2024 Gmail và Yahoo bắt buộc người gửi số lượng lớn hỗ trợ hủy một chạm. Không có nút hủy,
/// người nhận bấm "báo cáo spam" — tỷ lệ spam vượt 0.3% là mất reputation domain đang warm-up.
/// </summary>
public class ListUnsubscribeHeaderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static (EmailSenderService sut, CapturingHandler handler) Build()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MailJet:ApiKey"] = "key",
            ["MailJet:ApiSecret"] = "secret",
            ["MailJet:FromEmail"] = "no-reply@solarbattery.site",
        }).Build();

        var handler = new CapturingHandler();
        return (new EmailSenderService(config, new HttpClient(handler)), handler);
    }

    private static JsonElement FirstMessage(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("Messages")[0];

    [Fact]
    public async Task SendWithUnsubscribeHeaders_IncludesBothHeaders()
    {
        var (sut, handler) = Build();

        await sut.SendAsync("user@x.com", "Chủ đề", "<p>Nội dung</p>", new Dictionary<string, string>
        {
            ["List-Unsubscribe"] = "<https://api.solarbattery.site/api/notification-unsubscribe?token=abc>",
            ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
        });

        var headers = FirstMessage(handler.Body!).GetProperty("Headers");

        headers.GetProperty("List-Unsubscribe").GetString()
            .Should().Be("<https://api.solarbattery.site/api/notification-unsubscribe?token=abc>");

        // Thiếu header này thì Gmail chỉ mở trang web, không phải "một chạm" — không đạt quy định 2024.
        headers.GetProperty("List-Unsubscribe-Post").GetString()
            .Should().Be("List-Unsubscribe=One-Click");
    }

    /// <summary>
    /// Email giao dịch (OTP, đặt lại mật khẩu) KHÔNG được có nút hủy — người dùng không thể
    /// "hủy đăng ký" khỏi mã xác thực do chính họ vừa yêu cầu.
    /// </summary>
    [Fact]
    public async Task SendWithoutHeaders_OmitsHeadersField()
    {
        var (sut, handler) = Build();

        await sut.SendAsync("user@x.com", "Mã OTP", "<p>123456</p>");

        FirstMessage(handler.Body!).TryGetProperty("Headers", out _)
            .Should().BeFalse("payload email giao dịch phải giữ nguyên như trước");
    }

    [Fact]
    public async Task SendWithEmptyHeaderDictionary_OmitsHeadersField()
    {
        var (sut, handler) = Build();

        await sut.SendAsync("user@x.com", "Chủ đề", "<p>x</p>", new Dictionary<string, string>());

        FirstMessage(handler.Body!).TryGetProperty("Headers", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SendWithHeaders_StillCarriesCoreFields()
    {
        var (sut, handler) = Build();

        await sut.SendAsync("user@x.com", "Chủ đề", "<p>Nội dung</p>", new Dictionary<string, string>
        {
            ["List-Unsubscribe"] = "<https://x/y>",
        });

        var message = FirstMessage(handler.Body!);
        message.GetProperty("To")[0].GetProperty("Email").GetString().Should().Be("user@x.com");
        message.GetProperty("Subject").GetString().Should().Be("Chủ đề");
        message.GetProperty("HTMLPart").GetString().Should().Be("<p>Nội dung</p>");
    }
}
