using BatteryService.Application.Interfaces;
using BatteryService.Application.Mqtt;
using BatteryService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// GH-784 — đồng bộ thông tin đăng nhập thiết bị từ DB xuống file <c>passwd</c> của Mosquitto.
/// </summary>
/// <remarks>
/// <para>
/// Trước đây API sinh username/password, lưu DB, trả cho thiết bị — nhưng broker KHÔNG hề biết.
/// Thiết bị nối vào và nhận "Connection Refused: not authorised", trong khi mọi tầng phía trên đều
/// báo thành công.
/// </para>
/// <para>
/// Broker nạp lại file thế nào là việc của phía triển khai: container Mosquitto tự theo dõi file và
/// tự gửi SIGHUP cho chính nó (xem <c>docker-compose.yml</c>). Chọn cách đó thay vì để service này
/// gọi Docker API, vì gọi Docker API nghĩa là trao quyền điều khiển toàn máy chủ cho một container
/// ứng dụng — quá đắt cho việc chỉ cần bảo broker đọc lại một file.
/// </para>
/// </remarks>
public class MqttPasswordFileSyncService : BackgroundService
{
    /// <summary>
    /// Trạng thái ĐƯỢC phép nối broker.
    /// </summary>
    /// <remarks>
    /// <c>Offline</c> vẫn được: mất kết nối là chuyện tạm thời, rút quyền của nó sẽ khiến thiết bị
    /// không bao giờ nối lại được. <c>Disabled</c>/<c>Decommissioned</c> thì KHÔNG — đó chính là ý
    /// nghĩa của việc vô hiệu hoá, để lại trong file thì thu hồi chỉ có tác dụng trên giấy tờ.
    /// </remarks>
    private static readonly IotDeviceStatusEnum[] AllowedStatuses =
    [
        IotDeviceStatusEnum.Pending,
        IotDeviceStatusEnum.Active,
        IotDeviceStatusEnum.Offline,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttPasswordFileSyncService> _logger;

    public MqttPasswordFileSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<MqttOptions> options,
        ILogger<MqttPasswordFileSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.PasswordFilePath))
        {
            _logger.LogInformation(
                "MqttPasswordFileSync tắt (Mqtt:Enabled={Enabled}, Mqtt:PasswordFilePath chưa đặt) — "
                + "thông tin đăng nhập thiết bị sẽ KHÔNG tới được broker.",
                _options.Enabled);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.CredentialSyncIntervalSeconds));
        _logger.LogInformation(
            "MqttPasswordFileSync started (file={File}, interval={Seconds}s)",
            _options.PasswordFilePath, interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Không để một lượt hỏng làm chết vòng quét: lần sau vẫn phải thử lại, nếu không
                // thiết bị cấp mới sau đó sẽ không bao giờ vào được broker.
                _logger.LogError(ex, "MqttPasswordFileSync tick failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }

        _logger.LogInformation("MqttPasswordFileSync stopped");
    }

    /// <summary>Chạy đúng MỘT lượt đồng bộ.</summary>
    /// <remarks>
    /// Để <c>public</c> có chủ ý: đây là seam để test kiểm được hành vi mà không phải chờ vòng lặp,
    /// và cũng là đường để kích hoạt đồng bộ ngay sau khi cấp/thu hồi thiết bị nếu sau này cần —
    /// thay vì đợi hết chu kỳ rà soát.
    /// </remarks>
    public async Task SyncOnceAsync(CancellationToken ct)
    {
        var path = _options.PasswordFilePath!;

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();

        var devices = await uow.IotDevices
            .GetAllAsync()
            .Where(d => !d.IsDeleted
                        && AllowedStatuses.Contains(d.Status)
                        && d.MqttUsername != null
                        && d.MqttPasswordHash != null)
            .Select(d => new { d.MqttUsername, d.MqttPasswordHash })
            .ToListAsync(ct);

        var entries = devices
            .Select(d => new MosquittoCredential(d.MqttUsername!, d.MqttPasswordHash!))
            .ToList();

        var existing = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
        var desired = MosquittoPasswordFile.Compose(existing, entries);

        if (string.Equals(existing, desired, StringComparison.Ordinal))
            return;   // không đổi ⇒ không ghi ⇒ không bắt broker nạp lại vô cớ

        await WriteAtomicallyAsync(path, desired, ct);

        _logger.LogInformation(
            "MqttPasswordFileSync: đã ghi {Count} bản ghi thiết bị vào {File}.", entries.Count, path);
    }

    /// <summary>
    /// Ghi qua file tạm rồi đổi tên — broker không bao giờ đọc phải file ghi dở.
    /// </summary>
    /// <remarks>
    /// Ghi thẳng lên file đang được mount sẽ có khoảnh khắc nội dung mới một nửa; nếu đúng lúc đó
    /// broker nạp lại thì nó gặp bản ghi hỏng và TỪ CHỐI CẢ FILE — mất quyền của mọi thiết bị.
    /// <para>Quyền 0600: Mosquitto 2.0 từ chối nạp file mà người khác đọc được.</para>
    /// </remarks>
    private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content, ct);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, path, overwrite: true);
    }
}
