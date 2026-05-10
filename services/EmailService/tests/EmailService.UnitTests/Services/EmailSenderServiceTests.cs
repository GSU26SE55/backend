using System.Net;
using System.Text;
using EmailService.Infrastructure.Services;
using EmailService.UnitTests.Helpers;
using Microsoft.Extensions.Configuration;

namespace EmailService.UnitTests.Services;

/// <summary>
/// Test EmailSenderService trực tiếp: validation input, build payload Mailjet, basic auth header,
/// throw khi response non-2xx.
/// </summary>
public class EmailSenderServiceTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?>? overrides = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["MailJet:ApiKey"] = "test-key",
            ["MailJet:ApiSecret"] = "test-secret",
            ["MailJet:FromEmail"] = "noreply@test.local",
            ["MailJet:DisplayName"] = "TestApp",
            ["MailJet:SendEndpoint"] = "https://fake.mailjet.local/v3.1/send"
        };
        if (overrides != null)
            foreach (var kv in overrides)
                dict[kv.Key] = kv.Value;

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (EmailSenderService sut, FakeHttpMessageHandler handler) NewSut(
        IConfiguration? config = null,
        HttpStatusCode responseStatus = HttpStatusCode.OK,
        string responseBody = "{\"Sent\":[]}")
    {
        var handler = new FakeHttpMessageHandler { ResponseStatus = responseStatus, ResponseBody = responseBody };
        var http = new HttpClient(handler);
        return (new EmailSenderService(config ?? BuildConfig(), http), handler);
    }

    [Fact]
    public async Task SendAsync_EmptyTo_ThrowsArgumentException()
    {
        var (sut, _) = NewSut();
        var act = async () => await sut.SendAsync(" ", "subject", "<p>body</p>");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Recipient*");
    }

    [Fact]
    public async Task SendAsync_EmptySubject_ThrowsArgumentException()
    {
        var (sut, _) = NewSut();
        var act = async () => await sut.SendAsync("a@b.com", "", "<p>body</p>");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Subject*");
    }

    [Fact]
    public async Task SendAsync_EmptyBody_ThrowsArgumentException()
    {
        var (sut, _) = NewSut();
        var act = async () => await sut.SendAsync("a@b.com", "subject", "");
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Body*");
    }

    [Fact]
    public async Task SendAsync_MissingApiKey_ThrowsInvalidOperation()
    {
        var (sut, _) = NewSut(BuildConfig(new() { ["MailJet:ApiKey"] = null }));
        var act = async () => await sut.SendAsync("a@b.com", "subject", "body");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*credentials*");
    }

    [Fact]
    public async Task SendAsync_MissingFromEmail_ThrowsInvalidOperation()
    {
        var (sut, _) = NewSut(BuildConfig(new() { ["MailJet:FromEmail"] = null }));
        var act = async () => await sut.SendAsync("a@b.com", "subject", "body");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Sender*");
    }

    [Fact]
    public async Task SendAsync_PostsToConfiguredEndpoint_WithBasicAuthAndJsonPayload()
    {
        var (sut, handler) = NewSut();

        await sut.SendAsync("user@example.com", "Hello!", "<h1>Body HTML</h1>");

        handler.CallCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://fake.mailjet.local/v3.1/send");

        // Basic auth = base64(apiKey:apiSecret)
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-key:test-secret"));
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(expected);

        // Mailjet payload shape (PascalCase keys preserved by JsonSerializer with PropertyNamingPolicy=null)
        handler.LastRequestBody.Should().Contain("\"From\":");
        handler.LastRequestBody.Should().Contain("noreply@test.local");
        handler.LastRequestBody.Should().Contain("TestApp");
        handler.LastRequestBody.Should().Contain("user@example.com");
        handler.LastRequestBody.Should().Contain("Hello!");
        handler.LastRequestBody.Should().Contain("Body HTML");
    }

    [Fact]
    public async Task SendAsync_FallbackKeysAreUsed_WhenMailJetMissing()
    {
        // Test fallback chain: Mailjet:ApiKey, Email:From, Email:DisplayName.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mailjet:ApiKey"] = "k",
            ["Mailjet:ApiSecret"] = "s",
            ["Email:From"] = "from@fallback.com",
            ["Email:DisplayName"] = "FallbackApp",
            ["MailJet:SendEndpoint"] = "https://fake.local/send"
        }).Build();

        var (sut, handler) = NewSut(config);

        await sut.SendAsync("to@x.com", "Subj", "Body");

        handler.LastRequestBody.Should().Contain("from@fallback.com");
        handler.LastRequestBody.Should().Contain("FallbackApp");
    }

    [Fact]
    public async Task SendAsync_DefaultEndpoint_WhenNoneConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MailJet:ApiKey"] = "k",
            ["MailJet:ApiSecret"] = "s",
            ["MailJet:FromEmail"] = "f@x.com"
        }).Build();
        var (sut, handler) = NewSut(config);

        await sut.SendAsync("to@x.com", "Subj", "Body");

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.mailjet.com/v3.1/send");
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_ThrowsHttpRequestException_WithBodyDetail()
    {
        var (sut, _) = NewSut(responseStatus: HttpStatusCode.BadRequest, responseBody: "{\"error\":\"bad\"}");

        var act = async () => await sut.SendAsync("a@b.com", "subject", "body");

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("400").And.Contain("bad");
    }

    [Fact]
    public async Task SendAsync_CancelledToken_ThrowsTaskCanceled()
    {
        var (sut, _) = NewSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.SendAsync("a@b.com", "subject", "body", cts.Token);
        await act.Should().ThrowAsync<TaskCanceledException>();
    }
}
