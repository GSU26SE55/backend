using System.Text.Json;
using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// Multi-event scenarios end-to-end: serialization round-trip, event ordering,
/// consumer độc lập theo từng event type.
/// </summary>
[Collection("EmailServiceIntegration")]
public class MultiEventScenarioTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public MultiEventScenarioTests(EmailServiceFactory factory)
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
    public async Task PublishMultipleRegisterEvents_AllConsumersRun_AllEmailsSent()
    {
        var events = Enumerable.Range(0, 5)
            .Select(i => new SendOtpRegisterEvent(
                $"multi-{Guid.NewGuid():N}@x.com",
                $"M{Random.Shared.Next(100000, 999999)}-{i}"))
            .ToList();

        foreach (var e in events)
            await _harness.Bus.Publish(e);

        // Đợi từng OTP unique của mỗi event được render → chứng minh consumer chạy hết.
        foreach (var e in events)
            await _factory.WaitForRenderCallAsync(e.Otp);

        // Render xảy ra trước lời gọi HTTP tới Mailjet. Nếu đếm ngay sau khi render,
        // request cuối cùng có thể vẫn đang chạy và làm test đỏ giả trên CI.
        // Đợi từng marker email unique xuất hiện trước khi assert exactly-once.
        foreach (var e in events)
            await _factory.WaitForMailjetCallAsync(e.ToEmail, timeoutMs: 10000);

        // Mỗi event tương ứng 1 Mailjet call.
        foreach (var e in events)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1, $"chính xác 1 mail cho {e.ToEmail}");
    }

    [Fact]
    public async Task PublishDifferentEventTypes_EachRoutedToCorrectConsumer()
    {
        // Mỗi consumer xử lý loại event riêng → publish 3 loại khác nhau, verify cả 3 đều chạy.
        var registerOtp = $"R{Random.Shared.Next(100000, 999999)}";
        var resetOtp = $"P{Random.Shared.Next(100000, 999999)}";
        var changeOtp = $"E{Random.Shared.Next(100000, 999999)}";

        await _harness.Bus.Publish(new SendOtpRegisterEvent($"reg-{Guid.NewGuid():N}@x.com", registerOtp));
        await _harness.Bus.Publish(new SendPasswordResetOtpEvent($"reset-{Guid.NewGuid():N}@x.com", resetOtp));
        await _harness.Bus.Publish(new SendEmailChangeOtpEvent($"change-{Guid.NewGuid():N}@x.com", changeOtp));

        await _factory.WaitForRenderCallAsync(registerOtp);
        await _factory.WaitForRenderCallAsync(resetOtp);
        await _factory.WaitForRenderCallAsync(changeOtp);

        // Mỗi OTP được render đúng template tương ứng.
        _factory.RenderCallsForOtp(registerOtp).Should().ContainSingle()
            .Which.TemplateName.Should().Be(EmailService.Infrastructure.Templates.EmailTemplates.OtpRegister);
        _factory.RenderCallsForOtp(resetOtp).Should().ContainSingle()
            .Which.TemplateName.Should().Be(EmailService.Infrastructure.Templates.EmailTemplates.OtpPasswordReset);
        _factory.RenderCallsForOtp(changeOtp).Should().ContainSingle()
            .Which.TemplateName.Should().Be(EmailService.Infrastructure.Templates.EmailTemplates.OtpEmailChange);
    }

    [Fact]
    public async Task EventPayload_PreservedThroughBusSerializationRoundTrip()
    {
        // Verify JSON serialize/deserialize của MassTransit không corrupt event fields:
        // unicode + emoji ở Otp. Email dùng ASCII (Mailjet body escapes unicode → marker substring
        // search có thể miss); chứng minh round-trip qua renderer + JSON body parse.
        var email = $"unicode-{Guid.NewGuid():N}@example.com";
        var otp = $"😀U{Random.Shared.Next(100000, 999999)}😀";

        await _harness.Bus.Publish(new SendOtpRegisterEvent(email, otp));

        await _factory.WaitForRenderCallAsync(otp);
        await _factory.WaitForMailjetCallAsync(email);

        // Renderer nhận unicode/emoji nguyên vẹn (không qua JSON encode khâu này).
        var renderCall = _factory.RenderCallsForOtp(otp).Should().ContainSingle().Subject;
        renderCall.Values["UserName"].Should().Be(email);
        renderCall.Values["Otp"].Should().Be(otp);

        // JSON body Mailjet hợp lệ + parse → khôi phục lại unicode/emoji đầy đủ.
        var mailReq = _factory.MailjetHandler.Requests.First(r => r.Body != null && r.Body.Contains(email));
        var doc = JsonDocument.Parse(mailReq.Body!);
        var html = doc.RootElement.GetProperty("Messages")[0].GetProperty("HTMLPart").GetString();
        html.Should().Contain(otp, "JSON deserialize phải khôi phục lại unicode/emoji nguyên vẹn");
    }
}
