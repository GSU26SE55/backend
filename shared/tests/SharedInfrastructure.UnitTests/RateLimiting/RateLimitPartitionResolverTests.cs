using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedInfrastructure.RateLimiting;
using Xunit;

namespace SharedInfrastructure.UnitTests.RateLimiting;

/// <summary>
/// Phân loại request thành bậc hạn mức nào — nơi quyết định con số 60 hay 500 được áp.
/// </summary>
public class RateLimitPartitionResolverTests
{
    private static readonly StandardRateLimitOptions Options = new();

    [Fact]
    public void Defaults_MatchAgreedLimits()
    {
        Options.AnonymousPermitLimit.Should().Be(60);
        Options.AuthenticatedPermitLimit.Should().Be(500);
        Options.WindowSeconds.Should().Be(30);
        Options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Anonymous_Gets60_PartitionedByIp()
    {
        var decision = RateLimitPartitionResolver.Resolve(Context(ip: "203.0.113.7"), Options);

        decision.IsAuthenticated.Should().BeFalse();
        decision.PermitLimit.Should().Be(60);
        decision.PartitionKey.Should().Be("anon:203.0.113.7");
    }

    [Fact]
    public void Anonymous_DifferentIps_GetDifferentPartitions()
    {
        var a = RateLimitPartitionResolver.Resolve(Context(ip: "203.0.113.7"), Options);
        var b = RateLimitPartitionResolver.Resolve(Context(ip: "203.0.113.8"), Options);

        a.PartitionKey.Should().NotBe(b.PartitionKey);
    }

    [Fact]
    public void Authenticated_Gets500_PartitionedByAccountId()
    {
        var accountId = Guid.NewGuid().ToString();
        var decision = RateLimitPartitionResolver.Resolve(
            Context(ip: "203.0.113.7", claims: [new Claim("AccountId", accountId)]),
            Options);

        decision.IsAuthenticated.Should().BeTrue();
        decision.PermitLimit.Should().Be(500);
        decision.PartitionKey.Should().Be("user:" + accountId);
    }

    [Fact]
    public void Authenticated_DifferentUsersSameIp_DoNotShareQuota()
    {
        var context = Context(ip: "203.0.113.7", claims: [new Claim("AccountId", "user-a")]);
        var other = Context(ip: "203.0.113.7", claims: [new Claim("AccountId", "user-b")]);

        RateLimitPartitionResolver.Resolve(context, Options).PartitionKey
            .Should().NotBe(RateLimitPartitionResolver.Resolve(other, Options).PartitionKey);
    }

    /// <summary>
    /// Đây chính là lỗi cũ của gateway: khoá phân vùng đọc claim <c>UserId</c> mà JWT hệ thống không
    /// hề phát ra (claim thật là <c>AccountId</c>), nên mọi người dùng dùng chung một bộ đếm theo IP.
    /// </summary>
    [Fact]
    public void Authenticated_WithRealJwtClaimShape_IsIdentifiedNotFallenBackToIp()
    {
        var context = Context(ip: "203.0.113.7", claims:
        [
            new Claim("AccountId", "62b55455-db76-46c3-a79b-1a58e6b1998e"),
            new Claim(ClaimTypes.NameIdentifier, "62b55455-db76-46c3-a79b-1a58e6b1998e"),
            new Claim("FullName", "Demo Admin"),
            new Claim(ClaimTypes.Role, "Admin")
        ]);

        var decision = RateLimitPartitionResolver.Resolve(context, Options);

        decision.PartitionKey.Should().Be("user:62b55455-db76-46c3-a79b-1a58e6b1998e");
        decision.PartitionKey.Should().NotContain("203.0.113.7");
    }

    [Fact]
    public void IotDevice_AuthenticatedByApiKeyScheme_Gets500_KeyedByDevice()
    {
        // ApiKeyAuthenticationHandler của BatteryService phát NameIdentifier + iot:device_id.
        var context = Context(ip: "10.0.0.9", claims:
        [
            new Claim(ClaimTypes.NameIdentifier, "device-1"),
            new Claim("iot:device_id", "device-1"),
            new Claim(ClaimTypes.Role, "IotDevice")
        ]);

        var decision = RateLimitPartitionResolver.Resolve(context, Options);

        decision.PermitLimit.Should().Be(500);
        decision.PartitionKey.Should().Be("user:device-1");
    }

    [Fact]
    public void SmsGatewayDevice_KeyedByDeviceCode()
    {
        var context = Context(ip: "10.0.0.9", claims: [new Claim("device_code", "GW-01")]);

        var decision = RateLimitPartitionResolver.Resolve(context, Options);

        decision.PermitLimit.Should().Be(500);
        decision.PartitionKey.Should().Be("user:GW-01");
    }

    [Fact]
    public void Authenticated_WithoutIdentityClaim_KeepsHigherLimitButGroupsByIp()
    {
        var context = Context(ip: "203.0.113.7", claims: [new Claim("irrelevant", "x")]);

        var decision = RateLimitPartitionResolver.Resolve(context, Options);

        decision.PermitLimit.Should().Be(500);
        decision.PartitionKey.Should().Be("auth-ip:203.0.113.7");
    }

    /// <summary>
    /// Token rác không được nâng bậc. Nếu căn cứ là header <c>Authorization</c> thay vì kết quả xác thực
    /// thì ai gắn một chuỗi bất kỳ cũng nhảy từ 60 lên 500.
    /// </summary>
    [Fact]
    public void InvalidToken_IsTreatedAsAnonymous()
    {
        var context = Context(ip: "203.0.113.7");
        context.Request.Headers["Authorization"] = "Bearer khong-phai-token-that";

        var decision = RateLimitPartitionResolver.Resolve(context, Options);

        decision.IsAuthenticated.Should().BeFalse();
        decision.PermitLimit.Should().Be(60);
    }

    [Fact]
    public void AnonymousPartition_PrefersGatewayClientIpHeader()
    {
        // Sau gateway, RemoteIpAddress luôn là IP container gateway; header mới là IP thật.
        var context = Context(ip: "172.18.0.5");
        context.Request.Headers[RateLimitPartitionResolver.ClientIpHeader] = "203.0.113.99";

        RateLimitPartitionResolver.Resolve(context, Options).PartitionKey
            .Should().Be("anon:203.0.113.99");
    }

    [Fact]
    public void AnonymousPartition_FallsBackToRemoteIp_WhenHeaderMissing()
    {
        RateLimitPartitionResolver.Resolve(Context(ip: "172.18.0.5"), Options).PartitionKey
            .Should().Be("anon:172.18.0.5");
    }

    [Fact]
    public void UnknownIp_StillProducesStableKey()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tickets";

        RateLimitPartitionResolver.Resolve(context, Options).PartitionKey
            .Should().Be("anon:" + RateLimitPartitionResolver.UnknownClient);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/live")]
    [InlineData("/ready")]
    [InlineData("/metrics")]
    [InlineData("/api/ticket/health")]
    [InlineData("/api/battery/health")]
    [InlineData("/swagger/index.html")]
    [InlineData("/ticket-service/swagger/v1/swagger.json")]
    public void InfrastructureEndpoints_AreExempt(string path)
    {
        var context = Context(ip: "127.0.0.1");
        context.Request.Path = path;

        RateLimitPartitionResolver.Resolve(context, Options).IsExempt.Should().BeTrue(
            "health check và metrics bị chặn sẽ làm container bị đánh dấu unhealthy rồi khởi động lại");
    }

    [Theory]
    [InlineData("/api/tickets")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/internal/knowledge-base/abc")]
    [InlineData("/api/files/upload")]
    public void BusinessEndpoints_AreNotExempt(string path)
    {
        var context = Context(ip: "203.0.113.7");
        context.Request.Path = path;

        RateLimitPartitionResolver.Resolve(context, Options).IsExempt.Should().BeFalse();
    }

    /// <summary>
    /// gRPC nội bộ không gắn <c>[Authorize]</c> nên nếu bị tính hạn mức sẽ rơi vào bậc ẩn danh 60 —
    /// một service gọi service khác vài chục lần là tự làm nghẽn chính mình.
    /// </summary>
    [Theory]
    [InlineData("application/grpc")]
    [InlineData("application/grpc+proto")]
    [InlineData("application/grpc-web")]
    [InlineData("APPLICATION/GRPC")]
    public void InternalGrpcCalls_AreExempt(string contentType)
    {
        var context = Context(ip: "172.18.0.4");
        context.Request.Path = "/battery.BatteryInternal/GetAsset";
        context.Request.ContentType = contentType;

        RateLimitPartitionResolver.Resolve(context, Options).IsExempt.Should().BeTrue();
    }

    [Fact]
    public void RegularJsonPost_IsNotMistakenForGrpc()
    {
        var context = Context(ip: "203.0.113.7");
        context.Request.ContentType = "application/json";

        RateLimitPartitionResolver.Resolve(context, Options).IsExempt.Should().BeFalse();
    }

    [Fact]
    public void Grpc_CanBeBroughtBackUnderTheLimit_ByConfiguration()
    {
        var strict = new StandardRateLimitOptions { ExemptGrpc = false };
        var context = Context(ip: "172.18.0.4");
        context.Request.ContentType = "application/grpc";

        RateLimitPartitionResolver.Resolve(context, strict).IsExempt.Should().BeFalse();
    }

    [Fact]
    public void Disabled_MakesEverythingExempt()
    {
        var disabled = new StandardRateLimitOptions { Enabled = false };

        RateLimitPartitionResolver.Resolve(Context(ip: "203.0.113.7"), disabled)
            .IsExempt.Should().BeTrue();
    }

    private static DefaultHttpContext Context(string ip, Claim[]? claims = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tickets";
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        if (claims is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestScheme"));

        return context;
    }
}
