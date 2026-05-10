using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// Khi consumer throw, MassTransit retry. Test bằng cách config FakeHttpMessageHandler trả 500
/// trong vài request đầu, rồi 200. Verify retry → eventually success / hoặc all fail.
/// </summary>
[Collection("EmailServiceIntegration")]
public class ConsumerRetryTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public ConsumerRetryTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        _harness = await _factory.GetHarnessAsync();
    }

    public Task DisposeAsync()
    {
        // Restore mặc định 200 sau khi test set 500.
        _factory.MailjetHandler.ResponseStatus = System.Net.HttpStatusCode.OK;
        _factory.MailjetHandler.ResponseBody = "{\"Sent\":[]}";
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MailjetReturns500_ConsumerThrows_ErrorRecorded()
    {
        // Set Mailjet response = 500 → EmailSenderService throw HttpRequestException → consumer rethrow.
        _factory.MailjetHandler.ResponseStatus = System.Net.HttpStatusCode.InternalServerError;
        _factory.MailjetHandler.ResponseBody = "{\"error\":\"server\"}";

        var email = $"retry-{Guid.NewGuid():N}@x.com";
        var otp = $"RT{Random.Shared.Next(100000, 999999)}";
        var evt = new SendOtpRegisterEvent(email, otp);

        await _harness.Bus.Publish(evt);

        // Đợi đến khi event được record dưới Faulted (consumer throw).
        var deadline = DateTime.UtcNow.AddSeconds(10);
        bool faulted = false;
        while (DateTime.UtcNow < deadline)
        {
            await foreach (var item in _harness.Consumed.SelectAsync<SendOtpRegisterEvent>())
            {
                if (item.Context.Message.Otp == otp && item.Exception != null)
                {
                    faulted = true;
                    break;
                }
            }
            if (faulted)
                break;
            await Task.Delay(100);
        }

        faulted.Should().BeTrue("MassTransit phải record consumer faulted khi sender throw");

        // Mailjet phải bị gọi ít nhất 1 lần với event này (kể cả khi fail).
        _factory.CountMailjetCallsContaining(email).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MailjetSucceeds_NoConsumerFault_Recorded()
    {
        // Sanity check: nếu Mailjet 200, consumed log không có exception cho event này.
        _factory.MailjetHandler.ResponseStatus = System.Net.HttpStatusCode.OK;

        var email = $"ok-{Guid.NewGuid():N}@x.com";
        var otp = $"OK{Random.Shared.Next(100000, 999999)}";
        await _harness.Bus.Publish(new SendOtpRegisterEvent(email, otp));

        await _factory.WaitForMailjetCallAsync(email);

        // Tìm consumed entry cho event này — exception phải null.
        await foreach (var item in _harness.Consumed.SelectAsync<SendOtpRegisterEvent>(c => c.Context.Message.Otp == otp))
        {
            item.Exception.Should().BeNull();
            return; // pass khi gặp item đầu tiên (event Id unique).
        }

        // Nếu không tìm thấy → consumer chưa chạy (timeout).
        throw new Xunit.Sdk.XunitException($"No consumed entry found for OTP={otp}");
    }
}
