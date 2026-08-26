using EmailService.Infrastructure.Consumers;
using EmailService.Infrastructure.Templates;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// End-to-end cho thư chào mừng của luồng nhập dữ liệu bên thứ ba: publish
/// <see cref="SendPartnerImportWelcomeEvent"/> qua bus thật (InMemory), verify
/// <see cref="SendPartnerImportWelcomeConsumer"/> được assembly scan của Program.cs bắt được,
/// render đúng template và POST tới Mailjet.
/// </summary>
/// <remarks>
/// Ràng buộc quan trọng nhất mà bộ test này giữ: <b>thư không được chứa mật khẩu</b>. Tài khoản
/// import được tạo với một mật khẩu ngẫu nhiên không ai đọc, khách tự đặt lại qua màn "Quên mật
/// khẩu". Nếu ai đó sửa consumer để gửi kèm mật khẩu cho tiện thì test cuối cùng sẽ hỏng.
/// Mỗi test dùng email unique để tách captures khỏi các test khác chạy chung harness.
/// </remarks>
[Collection("EmailServiceIntegration")]
public class PartnerImportWelcomeEmailConsumerTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public PartnerImportWelcomeEmailConsumerTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        _harness = await _factory.GetHarnessAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_SendPartnerImportWelcomeEvent_RendersWelcomeTemplate_PostsToMailjet()
    {
        var email = $"partner-{Guid.NewGuid():N}@example.com";
        var evt = new SendPartnerImportWelcomeEvent(Guid.NewGuid(), email, "Nguyễn Văn Khách");

        await _harness.Bus.Publish(evt);

        await _factory.WaitForMailjetCallAsync(email);

        var renderCalls = RenderCallsFor(email);
        renderCalls.Should().ContainSingle();
        renderCalls[0].TemplateName.Should().Be(EmailTemplates.PartnerImportWelcome);
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "UserName" && kvp.Value == "Nguyễn Văn Khách");
        renderCalls[0].Values.Should().Contain(kvp => kvp.Key == "Email" && kvp.Value == email);

        var mailReqs = _factory.MailjetHandler.Requests
            .Where(r => r.Body != null && r.Body.Contains(email))
            .ToList();
        mailReqs.Should().ContainSingle();
        mailReqs[0].Method.Should().Be(HttpMethod.Post);
        mailReqs[0].Uri!.ToString().Should().Be("https://fake.mailjet.local/v3.1/send");
        mailReqs[0].AuthorizationScheme.Should().Be("Basic");

        var consumerHarness = _harness.GetConsumerHarness<SendPartnerImportWelcomeConsumer>();
        (await consumerHarness.Consumed.Any<SendPartnerImportWelcomeEvent>()).Should().BeTrue();
    }

    /// <summary>
    /// Đường dẫn đặt lại mật khẩu phải mang sẵn email của khách, và phải được escape đúng —
    /// email đối tác bàn giao có cả dấu cộng và ký tự cần mã hoá.
    /// </summary>
    [Fact]
    public async Task Publish_EmailNeedingEscaping_AcceptUrlCarriesEscapedEmail()
    {
        var marker = Guid.NewGuid().ToString("N");
        var email = $"khach+import-{marker}@example.com";
        var evt = new SendPartnerImportWelcomeEvent(Guid.NewGuid(), email, "Trần Thị Pin");

        await _harness.Bus.Publish(evt);
        // Chờ theo phần hex của địa chỉ. Bộ mã hoá mặc định của System.Text.Json ghi dấu cộng
        // dưới dạng chuỗi thoát Unicode trong payload Mailjet, nên dò nguyên địa chỉ sẽ trượt.
        await _factory.WaitForMailjetCallAsync(marker);

        var call = RenderCallsFor(email).Should().ContainSingle().Subject;
        var acceptUrl = call.Values["AcceptUrl"];

        acceptUrl.Should().NotBeNullOrWhiteSpace();
        acceptUrl.Should().Contain("forgot-password");
        // Dấu cộng phải thành %2B, nếu không thì trang nhận được email sai và luồng đặt lại mật khẩu chết.
        acceptUrl.Should().Contain(Uri.EscapeDataString(email));
        acceptUrl.Should().NotContain($"email={email}");
    }

    /// <summary>
    /// Đối tác bàn giao dữ liệu thường thiếu họ tên. Thiếu tên thì xưng hô bằng email,
    /// chứ không được để lời chào trống.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Publish_BlankFullName_FallsBackToEmailAsUserName(string fullName)
    {
        var email = $"noname-{Guid.NewGuid():N}@example.com";
        var evt = new SendPartnerImportWelcomeEvent(Guid.NewGuid(), email, fullName);

        await _harness.Bus.Publish(evt);
        await _factory.WaitForMailjetCallAsync(email);

        var call = RenderCallsFor(email).Should().ContainSingle().Subject;
        call.Values.Should().Contain(kvp => kvp.Key == "UserName" && kvp.Value == email);
    }

    /// <summary>
    /// Redelivery của cùng một event không được gửi thư lần hai — inbox store phải chặn.
    /// Import lô lớn chạy qua RabbitMQ, ở đó redelivery là chuyện thường ngày.
    /// </summary>
    [Fact]
    public async Task Publish_SameEventTwice_SendsOnlyOneEmail()
    {
        var email = $"once-{Guid.NewGuid():N}@example.com";
        var evt = new SendPartnerImportWelcomeEvent(Guid.NewGuid(), email, "Lê Văn Một Lần");

        await _harness.Bus.Publish(evt);
        await _factory.WaitForMailjetCallAsync(email);

        await _harness.Bus.Publish(evt);
        // Cho consumer thứ hai đủ thời gian chạy xong nếu nó có chạy.
        await Task.Delay(500);

        _factory.CountMailjetCallsContaining(email).Should().Be(1);
        RenderCallsFor(email).Should().ContainSingle();
    }

    /// <summary>
    /// Mật khẩu ngẫu nhiên sinh lúc cấp tài khoản không bao giờ được đi vào thư.
    /// Event không mang mật khẩu, nên thư chỉ được có đúng những giá trị dưới đây.
    /// </summary>
    [Fact]
    public async Task Publish_WelcomeEmail_NeverCarriesPasswordFields()
    {
        var email = $"nopwd-{Guid.NewGuid():N}@example.com";
        var evt = new SendPartnerImportWelcomeEvent(Guid.NewGuid(), email, "Phạm Bảo Mật");

        await _harness.Bus.Publish(evt);
        await _factory.WaitForMailjetCallAsync(email);

        var call = RenderCallsFor(email).Should().ContainSingle().Subject;
        call.Values.Keys.Should().BeEquivalentTo(new[] { "AppName", "UserName", "Email", "AcceptUrl" });
        call.Values.Should().NotContainKey("Password");
        call.Values.Should().NotContainKey("TempPassword");

        var body = _factory.MailjetHandler.Requests.Single(r => r.Body != null && r.Body.Contains(email)).Body!;
        body.Should().NotContain("password=", "thư chỉ được dẫn khách sang màn quên mật khẩu, không mang mật khẩu");
    }

    private IReadOnlyList<RenderCall> RenderCallsFor(string email)
        => _factory.Renderer.Calls
            .Where(c => c.TemplateName == EmailTemplates.PartnerImportWelcome
                        && c.Values.TryGetValue("Email", out var v)
                        && v == email)
            .ToList();
}
