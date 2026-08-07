using AuthService.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedInfrastructure.RateLimiting;

namespace AuthService.UnitTests.Infrastructure.RateLimiting;

/// <summary>
/// Smoke test cho 2 rate-limit policy: AnonOtp (5/phút theo IP), AuthOtp (3/phút theo userId).
/// Vượt limit → 429 với JSON body và header Retry-After.
/// </summary>
public class RateLimitingExtensionsTests
{
    private static IHostBuilder BuildHost(string policy)
    {
        return new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddLogging();
                    // Ráp GIỐNG production: hạn mức nền giữ OnRejected + RejectionStatusCode,
                    // policy OTP chỉ khai giới hạn riêng. Thiếu dòng dưới thì response 429 rơi về
                    // mặc định 503 của framework — đúng như lần đầu chạy sau khi gom OnRejected về một chỗ.
                    s.AddStandardRateLimiting(new ConfigurationBuilder().Build());
                    s.AddOtpRateLimiting(window: TimeSpan.FromHours(1));
                    s.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(ep =>
                    {
                        ep.MapPost("/test", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("ok");
                        }).RequireRateLimiting(policy);
                    });
                });
        });
    }

    [Fact]
    public async Task AnonOtp_AllowsFirst5_Then429()
    {
        using var host = await BuildHost(RateLimitingExtensions.PolicyAnonOtp).StartAsync();
        var client = host.GetTestClient();

        // 5 request đầu OK
        for (int i = 0; i < 5; i++)
        {
            var resp = await client.PostAsync("/test", new StringContent(""));
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
                because: $"request thứ {i + 1} trong window phải pass");
        }

        // Request thứ 6 → 429
        var rejected = await client.PostAsync("/test", new StringContent(""));
        rejected.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);

        // Đọc qua JSON thay vì so chuỗi thô: CommonResponseWriter (dùng chung cho mọi response lỗi
        // của hệ thống) escape ký tự ngoài ASCII thành \uXXXX, nên "Quá nhiều yêu cầu" không xuất hiện
        // nguyên văn trong body. Client parse JSON vẫn nhận đúng câu tiếng Việt.
        var body = System.Text.Json.JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("isSuccess").GetBoolean().Should().BeFalse();
        body.GetProperty("statusCode").GetInt32().Should().Be(429);
        body.GetProperty("message").GetString().Should().Be("Quá nhiều yêu cầu. Vui lòng thử lại sau.");
        body.GetProperty("data").GetProperty("errorCode").GetString().Should().Be("RATE_LIMITED");
    }

    [Fact]
    public async Task AuthOtp_PermitLimit_IsLowerThanAnon()
    {
        using var host = await BuildHost(RateLimitingExtensions.PolicyAuthOtp).StartAsync();
        var client = host.GetTestClient();

        // AuthOtp = 3 req/phút (vì test không có User claim, fallback IP)
        for (int i = 0; i < 3; i++)
        {
            var resp = await client.PostAsync("/test", new StringContent(""));
            resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        }

        var rejected = await client.PostAsync("/test", new StringContent(""));
        rejected.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RejectedResponse_HasRetryAfterHeader()
    {
        using var host = await BuildHost(RateLimitingExtensions.PolicyAnonOtp).StartAsync();
        var client = host.GetTestClient();

        // Burst qua limit
        for (int i = 0; i < 6; i++)
            await client.PostAsync("/test", new StringContent(""));

        var rejected = await client.PostAsync("/test", new StringContent(""));
        rejected.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        rejected.Headers.Contains("Retry-After").Should().BeTrue();
    }

    [Fact]
    public async Task RejectedResponse_ContentType_IsJson()
    {
        using var host = await BuildHost(RateLimitingExtensions.PolicyAnonOtp).StartAsync();
        var client = host.GetTestClient();

        for (int i = 0; i < 6; i++)
            await client.PostAsync("/test", new StringContent(""));

        var rejected = await client.PostAsync("/test", new StringContent(""));
        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }
}
