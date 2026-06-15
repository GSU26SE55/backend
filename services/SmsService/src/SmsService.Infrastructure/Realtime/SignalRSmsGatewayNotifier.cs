using Microsoft.AspNetCore.SignalR;
using SmsService.Application.Interfaces.Services;

namespace SmsService.Infrastructure.Realtime;

/// <summary>
/// Impl <see cref="ISmsGatewayNotifier"/> dùng <c>IHubContext&lt;SmsGatewayHub&gt;</c>.
/// Push <c>NewPendingSms</c> + <c>BatchRevoked</c> tới đúng device hoặc broadcast.
/// </summary>
public class SignalRSmsGatewayNotifier : ISmsGatewayNotifier
{
    private readonly IHubContext<SmsGatewayHub> _hub;

    public SignalRSmsGatewayNotifier(IHubContext<SmsGatewayHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyNewPendingSmsAsync(Guid smsId, string phoneNumber, string? targetDeviceCode, CancellationToken cancellationToken = default)
    {
        var payload = new { smsId, phoneNumber, ts = DateTimeOffset.UtcNow };
        return targetDeviceCode is null
            ? _hub.Clients.Group(SmsGatewayHub.AllDevicesGroup).SendAsync("NewPendingSms", payload, cancellationToken)
            : _hub.Clients.Group(SmsGatewayHub.DeviceGroup(targetDeviceCode)).SendAsync("NewPendingSms", payload, cancellationToken);
    }

    public Task NotifyBatchRevokedAsync(IEnumerable<Guid> smsIds, string? targetDeviceCode, CancellationToken cancellationToken = default)
    {
        var payload = new { smsIds = smsIds.ToArray(), ts = DateTimeOffset.UtcNow };
        return targetDeviceCode is null
            ? _hub.Clients.Group(SmsGatewayHub.AllDevicesGroup).SendAsync("BatchRevoked", payload, cancellationToken)
            : _hub.Clients.Group(SmsGatewayHub.DeviceGroup(targetDeviceCode)).SendAsync("BatchRevoked", payload, cancellationToken);
    }
}
