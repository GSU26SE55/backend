using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.Persistence;
using SmsService.Infrastructure.Security;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.Security;

/// <summary>
/// <see cref="GatewayApiKeyAuthenticationHandler"/> — cổng xác thực RIÊNG cho thiết bị gateway
/// Android, không dùng chung JWT của người dùng. Trước bộ test này phủ 0%.
///
/// <para><b>Vì sao đáng kiểm kỹ:</b> đây là bề mặt xác thực duy nhất mà một thiết bị ngoài chạm
/// vào. Sai một nhánh là hoặc chặn nhầm thiết bị thật (SMS ngừng gửi), hoặc cho qua thiết bị đã bị
/// thu hồi (kẻ giữ khoá cũ vẫn đọc/gửi được tin nhắn).</para>
///
/// <para>Chạy trên DbContext THẬT vì handler tự truy vấn <c>SmsGatewayDevices</c> — điều kiện lọc
/// <c>IsActive &amp;&amp; !IsDeleted</c> là phần đáng kiểm nhất, mock đi thì mất luôn.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class GatewayApiKeyAuthenticationHandlerTests : IAsyncLifetime
{
    private const string ValidKey = "khoa-that-cua-thiet-bi";
    private const string DeviceCode = "GW-001";

    private readonly SmsPostgresFixture _db;
    public GatewayApiKeyAuthenticationHandlerTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Hasher giả lập: "hash" = tiền tố <c>h:</c>. Đủ để kiểm nhánh Verify đúng/sai.</summary>
    private sealed class PrefixHasher : IGatewayApiKeyHasher
    {
        public string Hash(string plain) => "h:" + plain;
        public bool Verify(string plain, string hash) => hash == "h:" + plain;
    }

    private async Task<SmsGatewayDevice> SeedDeviceAsync(
        string code = DeviceCode, string key = ValidKey, bool isActive = true, bool softDeleted = false)
    {
        var device = new SmsGatewayDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = code,
            DeviceName = "May gateway " + code,
            ApiKeyHash = "h:" + key,
            IsActive = isActive,
            DailyLimit = 100,
            IsDeleted = softDeleted,
        };

        await using var db = _db.NewContext();
        db.SmsGatewayDevices.Add(device);
        await db.SaveChangesAsync();
        return device;
    }

    /// <summary>
    /// Dựng handler thật trên một <see cref="HttpContext"/> thật. Không dùng
    /// <c>WebApplicationFactory</c> — cần điều khiển chính xác header/query, và không muốn kéo cả
    /// pipeline lên chỉ để kiểm một scheme xác thực.
    /// </summary>
    private async Task<AuthenticateResult> AuthenticateAsync(
        string? deviceCodeHeader = null, string? bearer = null,
        string? deviceCodeQuery = null, string? accessTokenQuery = null)
    {
        await using var db = _db.NewContext();

        var handler = new GatewayApiKeyAuthenticationHandler(
            new OptionsMonitorStub(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            db,
            new PrefixHasher());

        var http = new DefaultHttpContext();
        if (deviceCodeHeader is not null)
            http.Request.Headers["X-Device-Code"] = deviceCodeHeader;
        if (bearer is not null)
            http.Request.Headers.Authorization = bearer;

        var query = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();
        if (deviceCodeQuery is not null)
            query["deviceCode"] = deviceCodeQuery;
        if (accessTokenQuery is not null)
            query["access_token"] = accessTokenQuery;
        if (query.Count > 0)
            http.Request.Query = new QueryCollection(query);

        await handler.InitializeAsync(
            new AuthenticationScheme(GatewayAuthOptions.SchemeName, null, typeof(GatewayApiKeyAuthenticationHandler)),
            http);

        return await handler.AuthenticateAsync();
    }

    // ──────────────────────────────────────────────────────────── cho qua

    [Fact]
    public async Task RestPath_ValidHeaderAndBearer_Succeeds_WithDeviceClaims()
    {
        var device = await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: $"Bearer {ValidKey}");

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.FindFirstValue("device_code").Should().Be(DeviceCode);
        principal.FindFirstValue("device_id").Should().Be(device.Id.ToString());
        principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be(device.Id.ToString());
        principal.FindFirstValue(ClaimTypes.Name).Should().Be(device.DeviceName);
    }

    /// <summary>
    /// Đường SignalR/WebSocket: trình duyệt không gửi được header tuỳ ý khi mở WebSocket, nên
    /// thông tin đi qua query string. Đây là nhánh riêng, dễ bị bỏ quên khi sửa handler.
    /// </summary>
    [Fact]
    public async Task WebSocketPath_ValidQueryParams_Succeeds()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeQuery: DeviceCode, accessTokenQuery: ValidKey);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task BearerPrefix_IsCaseInsensitive()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: $"bearer {ValidKey}");

        result.Succeeded.Should().BeTrue("RFC 7235 quy định scheme không phân biệt hoa thường");
    }

    [Fact]
    public async Task BearerToken_IsTrimmed()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: $"Bearer   {ValidKey}   ");

        result.Succeeded.Should().BeTrue("khoảng trắng thừa quanh token là lỗi sao chép rất phổ biến");
    }

    // ──────────────────────────────────────────────────────────── chặn lại

    [Fact]
    public async Task MissingDeviceCode_Fails()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(bearer: $"Bearer {ValidKey}");

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("X-Device-Code");
    }

    [Fact]
    public async Task MissingToken_Fails()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Missing Bearer token");
    }

    [Fact]
    public async Task UnknownDeviceCode_Fails()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: "GW-KHONG-CO", bearer: $"Bearer {ValidKey}");

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Unknown or revoked device");
    }

    /// <summary>
    /// Thiết bị đã bị thu hồi (<c>IsActive = false</c>) phải bị chặn NGAY, kể cả khi khoá vẫn đúng.
    /// Đây chính là điều làm cho nút "thu hồi" có ý nghĩa.
    /// </summary>
    [Fact]
    public async Task RevokedDevice_Fails_EvenWithCorrectKey()
    {
        await SeedDeviceAsync(isActive: false);

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: $"Bearer {ValidKey}");

        result.Succeeded.Should().BeFalse("thu hồi mà vẫn vào được thì việc thu hồi là vô nghĩa");
    }

    /// <summary>Thiết bị đã xoá mềm cũng phải bị chặn — bộ lọc <c>!IsDeleted</c> nằm trong truy vấn.</summary>
    [Fact]
    public async Task SoftDeletedDevice_Fails()
    {
        await SeedDeviceAsync(softDeleted: true);

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: $"Bearer {ValidKey}");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task WrongApiKey_Fails()
    {
        await SeedDeviceAsync();

        var result = await AuthenticateAsync(deviceCodeHeader: DeviceCode, bearer: "Bearer khoa-sai");

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("Invalid api key");
    }

    /// <summary>
    /// Mỗi thiết bị chỉ dùng được khoá CỦA MÌNH. Nếu handler quên ràng khoá với đúng thiết bị thì
    /// một thiết bị hợp lệ có thể mạo danh thiết bị khác.
    /// </summary>
    [Fact]
    public async Task KeyOfAnotherDevice_Fails()
    {
        await SeedDeviceAsync(code: "GW-A", key: "khoa-cua-A");
        await SeedDeviceAsync(code: "GW-B", key: "khoa-cua-B");

        var result = await AuthenticateAsync(deviceCodeHeader: "GW-A", bearer: "Bearer khoa-cua-B");

        result.Succeeded.Should().BeFalse("khoá của thiết bị B không được mở cho thiết bị A");
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<GatewayAuthOptions>
    {
        private readonly GatewayAuthOptions _options = new();
        public GatewayAuthOptions CurrentValue => _options;
        public GatewayAuthOptions Get(string? name) => _options;
        public IDisposable? OnChange(Action<GatewayAuthOptions, string?> listener) => null;
    }
}
