using System.Net;
using System.Web;
using AuthService.Infrastructure.Implements.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Test GoogleOAuthHelper:
/// - BuildAuthorizationUrl: query params, scopes, escape.
/// - ExchangeCodeForIdTokenAsync: HTTP shape, response parsing, error paths.
/// ValidateAsync gọi Google.Apis static lib → không test trực tiếp ở đây (integration only).
/// </summary>
public class GoogleOAuthHelperTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?>? overrides = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["GoogleOAuth:ClientId"] = "fake-client-id.apps.googleusercontent.com",
            ["GoogleOAuth:ClientSecret"] = "fake-client-secret",
            ["GoogleOAuth:Scope"] = "openid email profile"
        };
        if (overrides != null)
            foreach (var kv in overrides)
                dict[kv.Key] = kv.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static (GoogleOAuthHelper sut, RecordingHandler handler) NewSut(
        IConfiguration? config = null,
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "{\"access_token\":\"a\",\"id_token\":\"the-id-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
    {
        var handler = new RecordingHandler { ResponseStatus = status, ResponseBody = body };
        var http = new HttpClient(handler);
        return (new GoogleOAuthHelper(config ?? BuildConfig(), NullLogger<GoogleOAuthHelper>.Instance, http), handler);
    }

    // ============== BuildAuthorizationUrl ==============

    [Fact]
    public void BuildAuthorizationUrl_IncludesAllRequiredParams()
    {
        var (sut, _) = NewSut();
        var url = sut.BuildAuthorizationUrl("STATE-1", "https://app.local/cb");

        url.Should().StartWith("https://accounts.google.com/o/oauth2/v2/auth?");
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        query["client_id"].Should().Be("fake-client-id.apps.googleusercontent.com");
        query["redirect_uri"].Should().Be("https://app.local/cb");
        query["response_type"].Should().Be("code");
        query["scope"].Should().Be("openid email profile");
        query["state"].Should().Be("STATE-1");
        query["access_type"].Should().Be("offline");
        query["prompt"].Should().Be("select_account");
        query["include_granted_scopes"].Should().Be("true");
    }

    [Fact]
    public void BuildAuthorizationUrl_DefaultScope_WhenConfigMissing()
    {
        var (sut, _) = NewSut(BuildConfig(new() { ["GoogleOAuth:Scope"] = null }));
        var url = sut.BuildAuthorizationUrl("S", "https://x.local/cb");
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        q["scope"].Should().Be("openid email profile");
    }

    [Fact]
    public void BuildAuthorizationUrl_NoClientId_Throws()
    {
        Environment.SetEnvironmentVariable("GOOGLE_CLIENT_ID", null);
        var (sut, _) = NewSut(BuildConfig(new() { ["GoogleOAuth:ClientId"] = null }));

        var act = () => sut.BuildAuthorizationUrl("S", "https://x.local/cb");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ClientId*");
    }

    [Fact]
    public void BuildAuthorizationUrl_EscapesValues()
    {
        var (sut, _) = NewSut();
        var url = sut.BuildAuthorizationUrl("state with space", "https://app.local/cb?x=1&y=2");

        // raw url phải có giá trị đã escape.
        url.Should().Contain("state%20with%20space");
        url.Should().Contain("redirect_uri=https%3A%2F%2Fapp.local%2Fcb%3Fx%3D1%26y%3D2");
    }

    // ============== ExchangeCodeForIdTokenAsync ==============

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_EmptyCode_ReturnsNull()
    {
        var (sut, handler) = NewSut();
        var result = await sut.ExchangeCodeForIdTokenAsync(" ", "https://x.local/cb");
        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_MissingClientCredentials_ReturnsNull_NoHttpCall()
    {
        Environment.SetEnvironmentVariable("GOOGLE_CLIENT_ID", null);
        Environment.SetEnvironmentVariable("GOOGLE_CLIENT_SECRET", null);
        var (sut, handler) = NewSut(BuildConfig(new()
        {
            ["GoogleOAuth:ClientId"] = null,
            ["GoogleOAuth:ClientSecret"] = null
        }));

        var result = await sut.ExchangeCodeForIdTokenAsync("CODE", "https://x.local/cb");

        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_PostsFormToTokenEndpoint_WithRequiredFields()
    {
        var (sut, handler) = NewSut();

        var result = await sut.ExchangeCodeForIdTokenAsync("THE-CODE", "https://app.local/cb");

        result.Should().Be("the-id-token");
        handler.CallCount.Should().Be(1);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://oauth2.googleapis.com/token");
        handler.LastRequest.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");

        // Decode form body
        var form = HttpUtility.ParseQueryString(handler.LastRequestBody!);
        form["code"].Should().Be("THE-CODE");
        form["client_id"].Should().Be("fake-client-id.apps.googleusercontent.com");
        form["client_secret"].Should().Be("fake-client-secret");
        form["redirect_uri"].Should().Be("https://app.local/cb");
        form["grant_type"].Should().Be("authorization_code");
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_NonSuccessStatus_ReturnsNull()
    {
        var (sut, _) = NewSut(status: HttpStatusCode.BadRequest, body: "{\"error\":\"invalid_grant\"}");

        var result = await sut.ExchangeCodeForIdTokenAsync("CODE", "https://app.local/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_EmptyBody_ReturnsNull()
    {
        var (sut, _) = NewSut(body: "");

        var result = await sut.ExchangeCodeForIdTokenAsync("CODE", "https://app.local/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_BodyMissingIdToken_ReturnsNull()
    {
        var (sut, _) = NewSut(body: "{\"access_token\":\"a\",\"token_type\":\"Bearer\"}");

        var result = await sut.ExchangeCodeForIdTokenAsync("CODE", "https://app.local/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExchangeCodeForIdTokenAsync_HttpThrows_ReturnsNull_DoesNotPropagate()
    {
        var handler = new RecordingHandler { ThrowOnSend = new HttpRequestException("network down") };
        var http = new HttpClient(handler);
        var sut = new GoogleOAuthHelper(BuildConfig(), NullLogger<GoogleOAuthHelper>.Instance, http);

        var result = await sut.ExchangeCodeForIdTokenAsync("CODE", "https://app.local/cb");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_EmptyIdToken_ReturnsNull()
    {
        var (sut, _) = NewSut();
        var result = await sut.ValidateAsync(" ");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_GarbageToken_ReturnsNull_DoesNotThrow()
    {
        // Google.Apis lib sẽ throw khi parse — helper catch → null.
        var (sut, _) = NewSut();
        var result = await sut.ValidateAsync("not-a-real-jwt-token");
        result.Should().BeNull();
    }

    private class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
        public Exception? ThrowOnSend { get; set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowOnSend != null)
                throw ThrowOnSend;
            CallCount++;
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(ResponseStatus) { Content = new StringContent(ResponseBody) };
        }
    }
}
