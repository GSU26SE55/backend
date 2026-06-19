namespace SmsService.Application.Interfaces.Services;

/// <summary>
/// Abstraction để Application layer notify Flutter realtime mà KHÔNG reference Microsoft.AspNetCore.SignalR.
/// Infrastructure impl <c>SignalRSmsGatewayNotifier</c>.
/// Khi chưa cần realtime (unit test, sandbox) dùng <c>NullSmsGatewayNotifier</c>.
/// </summary>
public interface ISmsGatewayNotifier
{
    /// <summary>
    /// Push <c>NewPendingSms</c> tới device(s):
    /// <list type="bullet">
    ///   <item><paramref name="targetDeviceCode"/> = null → broadcast tới group <c>gateway:all</c>.</item>
    ///   <item><paramref name="targetDeviceCode"/> != null → chỉ device đó (group <c>device:{code}</c>).</item>
    /// </list>
    /// </summary>
    Task NotifyNewPendingSmsAsync(Guid smsId, string phoneNumber, string? targetDeviceCode, CancellationToken cancellationToken = default);

    /// <summary>Push <c>BatchRevoked</c> báo device cancel SMS đang ở trạng thái Pending.</summary>
    Task NotifyBatchRevokedAsync(IEnumerable<Guid> smsIds, string? targetDeviceCode, CancellationToken cancellationToken = default);
}

/// <summary>No-op notifier dùng cho unit test / sandbox.</summary>
public sealed class NullSmsGatewayNotifier : ISmsGatewayNotifier
{
    public Task NotifyNewPendingSmsAsync(Guid smsId, string phoneNumber, string? targetDeviceCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifyBatchRevokedAsync(IEnumerable<Guid> smsIds, string? targetDeviceCode, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
