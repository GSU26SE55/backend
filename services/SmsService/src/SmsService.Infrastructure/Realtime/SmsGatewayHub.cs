using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmsService.Infrastructure.Persistence;
using SmsService.Infrastructure.Security;

namespace SmsService.Infrastructure.Realtime;

/// <summary>
/// SignalR Hub cho Flutter <c>sms_fowarder</c> — auth qua scheme <c>"GatewayApiKey"</c>
/// (token đi qua query <c>access_token</c> + <c>deviceCode</c> khi handshake WebSocket).
/// <para>
/// Mỗi device khi connect được add vào group <c>device:{code}</c> + <c>gateway:all</c>.
/// Notifier push <c>NewPendingSms</c> tới <c>device:{code}</c> nếu có <c>TargetDeviceCode</c>,
/// hoặc <c>gateway:all</c> nếu broadcast.
/// </para>
/// </summary>
[Authorize(AuthenticationSchemes = GatewayAuthOptions.SchemeName)]
public class SmsGatewayHub : Hub
{
    public static string DeviceGroup(string code) => $"device:{code}";
    public const string AllDevicesGroup = "gateway:all";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsGatewayHub> _logger;

    public SmsGatewayHub(IServiceScopeFactory scopeFactory, ILogger<SmsGatewayHub> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var deviceCode = Context.User?.FindFirst("device_code")?.Value;
        var deviceIdStr = Context.User?.FindFirst("device_id")?.Value;

        if (string.IsNullOrEmpty(deviceCode) || !Guid.TryParse(deviceIdStr, out var deviceId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, DeviceGroup(deviceCode));
        await Groups.AddToGroupAsync(Context.ConnectionId, AllDevicesGroup);

        // Hub là transient nhưng connection sống lâu — phải tạo scope mới khi access DbContext.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var device = await db.SmsGatewayDevices.FirstOrDefaultAsync(x => x.Id == deviceId);
        if (device is not null)
        {
            device.Touch(Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString(), DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        _logger.LogInformation("[SmsGateway] Connected {DeviceCode} ({ConnId})", deviceCode, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var deviceCode = Context.User?.FindFirst("device_code")?.Value;
        _logger.LogInformation("[SmsGateway] Disconnected {DeviceCode} ({ConnId}) reason={Reason}",
            deviceCode, Context.ConnectionId, exception?.Message ?? "client close");
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Healthcheck client-side — Flutter có thể invoke để verify Hub still alive.</summary>
    public static Task<string> Ping() => Task.FromResult("pong");
}
