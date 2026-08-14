using System.Net;
using EmailService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace EmailService.UnitTests.Services;

/// <summary>
/// Mailjet Send API v3.1 xử lý từng message riêng và trả kết quả từng cái trong body — HTTP 200
/// KHÔNG đồng nghĩa thư đã được nhận ("the response can contain both success and error
/// notifications", dev.mailjet.com/email/guides/send-api-v31).
///
/// Trước đây <see cref="EmailSenderService"/> chỉ xét mã HTTP nên địa chỉ sai / sender chưa xác
/// thực / hết quota đều bị nuốt: log ghi "đã gửi" còn người nhận chờ mãi không thấy thư. Bộ test
/// này khoá lại hành vi đúng.
/// </summary>
public class MailjetPerMessageStatusTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }

    private static EmailSenderService Build(HttpStatusCode status, string body)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MailJet:ApiKey"] = "key",
            ["MailJet:ApiSecret"] = "secret",
            ["MailJet:FromEmail"] = "no-reply@solarbattery.site",
        }).Build();

        return new EmailSenderService(config, new HttpClient(new StubHandler(status, body)));
    }

    private static Task Send(EmailSenderService sut)
        => sut.SendAsync("user@example.com", "Chủ đề", "<p>Nội dung</p>");

    [Fact]
    public async Task Status_Success_KhongNemLoi()
    {
        var sut = Build(HttpStatusCode.OK, """
            {"Messages":[{"Status":"success","To":[{"Email":"user@example.com","MessageID":1}]}]}
            """);

        await Send(sut); // không ném là đạt
    }

    [Fact]
    public async Task Status_Error_TuyHTTP200_VanNemLoi()
    {
        // Đây chính là ca đã âm thầm nuốt lỗi trước khi sửa.
        var sut = Build(HttpStatusCode.OK, """
            {"Messages":[{"Status":"error","Errors":[{"ErrorCode":"mj-0013",
            "ErrorMessage":"\"user@example.com\" is an invalid email address."}]}]}
            """);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Send(sut));

        ex.Message.Should().Contain("user@example.com");
        // Nguyên văn lỗi của Mailjet phải còn trong message thì mới lần ra được nguyên nhân.
        ex.Message.Should().Contain("mj-0013");
    }

    [Fact]
    public async Task GuiHangLoat_MotMessageLoi_VanNemLoi()
    {
        var sut = Build(HttpStatusCode.OK, """
            {"Messages":[
              {"Status":"success"},
              {"Status":"error","Errors":[{"ErrorMessage":"quota exceeded"}]}
            ]}
            """);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Send(sut));
        ex.Message.Should().Contain("quota exceeded");
    }

    [Fact]
    public async Task HttpKhac2xx_VanNemLoiNhuCu()
    {
        var sut = Build(HttpStatusCode.Unauthorized, """{"ErrorMessage":"API key authentication failure"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Send(sut));
        ex.Message.Should().Contain("401");
    }

    [Theory]
    // Body rỗng, JSON không có "Messages", và body không phải JSON: đều đã có HTTP 2xx nên COI NHƯ
    // ĐẠT. Chặn ở đây sẽ biến một thay đổi định dạng phía Mailjet thành sự cố gửi thư hàng loạt.
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("""{"Messages":"không phải mảng"}""")]
    public async Task BodyKhongDocDuoc_KhongChanLuongGui(string body)
    {
        var sut = Build(HttpStatusCode.OK, body);

        await Send(sut); // không ném là đạt
    }

    [Fact]
    public async Task Status_KhongPhanBietHoaThuong()
    {
        var sut = Build(HttpStatusCode.OK, """{"Messages":[{"Status":"SUCCESS"}]}""");

        await Send(sut);
    }
}
