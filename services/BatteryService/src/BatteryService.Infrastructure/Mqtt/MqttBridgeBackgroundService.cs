using System.Text;
using System.Text.Json;
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Observability;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;

namespace BatteryService.Infrastructure.Mqtt;

/// <summary>
/// Sprint IoT-2 #IoT2-22..25 (S4-BE-02..05) — long-running connection tới Mosquitto/EMQX.
///
/// Subscribe (4 wildcard theo schema mới — overall.md §52.14):
/// <list type="bullet">
///   <item><c>solar/+/+/telemetry</c> — telemetry batch.</item>
///   <item><c>solar/+/heartbeat</c> — health.</item>
///   <item><c>solar/+/status</c> — LWT online/offline.</item>
///   <item><c>solar/+/cmd/ack</c> — ack downlink command.</item>
/// </list>
///
/// Publish (1 topic): <c>solar/{deviceCode}/cmd</c>.
/// </summary>
public class MqttBridgeBackgroundService : BackgroundService, IMqttBridgePublisher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttBridgeBackgroundService> _logger;
    private IManagedMqttClient? _client;

    public MqttBridgeBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MqttOptions> options,
        ILogger<MqttBridgeBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Đặt series ngay khi khởi động, kể cả khi bridge tắt. Gauge chưa được set thì Prometheus
        // không thấy series nào, và alert MqttBridgeDisconnected lại im lặng — đúng cái đang muốn
        // sửa. Phát 0/0 khi tắt là trạng thái rõ ràng: "có bridge, chủ đích không bật".
        IotMetrics.MqttBridgeEnabled.Set(_options.Enabled ? 1 : 0);
        IotMetrics.MqttBridgeConnected.Set(0);

        if (!_options.Enabled)
        {
            _logger.LogInformation("MQTT bridge disabled (Mqtt:Enabled=false).");
            return;
        }

        var factory = new MqttFactory();
        _client = factory.CreateManagedMqttClient();

        var clientOptionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Username, _options.Password)
            .WithCleanSession(false);

        if (_options.UseTls)
        {
            clientOptionsBuilder = clientOptionsBuilder.WithTlsOptions(o =>
            {
                o.UseTls();
                if (_options.AllowUntrustedCertificates)
                    o.WithAllowUntrustedCertificates();
            });
        }

        var managedOptions = new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptionsBuilder.Build())
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(_options.ReconnectIntervalSeconds))
            .Build();

        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _client.ConnectedAsync += async _ =>
        {
            await _client.SubscribeAsync(MqttTopicMap.TelemetryWildcard);
            await _client.SubscribeAsync(MqttTopicMap.HeartbeatWildcard);
            await _client.SubscribeAsync(MqttTopicMap.StatusWildcard);
            await _client.SubscribeAsync(MqttTopicMap.CommandAckWildcard);
            // Sprint IoT-2 #IoT2-22 — log message khớp acceptance spec ("connected to broker, 4 subscriptions").
            IotMetrics.MqttBridgeConnected.Set(1);
            // Sprint IoT-2 #IoT2-22 — log message khớp acceptance spec ("connected to broker, 4 subscriptions").
            _logger.LogInformation(
                "MQTT bridge connected to broker, 4 subscriptions ({Host}:{Port})",
                _options.Host, _options.Port);
        };
        _client.DisconnectedAsync += d =>
        {
            IotMetrics.MqttBridgeConnected.Set(0);
            _logger.LogWarning("MQTT bridge disconnected: {Reason}", d.Reason);
            return Task.CompletedTask;
        };

        await _client.StartAsync(managedOptions);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _client.StopAsync();
        }
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-25 — publish downlink command tới <c>solar/{dev}/cmd</c>.
    /// Throws <see cref="InvalidOperationException"/> nếu bridge chưa start (Mqtt:Enabled=false hoặc broker không reachable).
    /// Caller (controller) bắt exception → trả 503 Service Unavailable.
    /// </summary>
    public async Task PublishCommandAsync(string deviceCode, string payloadJson, CancellationToken ct = default)
    {
        if (_client is null || !_client.IsStarted)
        {
            _logger.LogWarning("Cannot publish cmd — MQTT bridge not started.");
            throw new InvalidOperationException("MQTT bridge chưa khởi động (Mqtt:Enabled=false hoặc broker không reachable).");
        }
        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(MqttTopicMap.Command(deviceCode))
            .WithPayload(payloadJson)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.EnqueueAsync(msg);
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.ToArray());

        if (!MqttTopicMap.TryParse(topic, out var deviceCode, out var kind, out var batterySerial))
        {
            _logger.LogDebug("Ignore MQTT message — unrecognized topic {Topic}", topic);
            return;
        }

        try
        {
            switch (kind)
            {
                case "telemetry":
                    await DispatchTelemetryAsync(deviceCode, batterySerial!, payload);
                    break;
                case "heartbeat":
                    await DispatchHeartbeatAsync(deviceCode, payload);
                    break;
                case "status":
                    await DispatchStatusAsync(deviceCode, payload);
                    break;
                case "cmd_ack":
                    DispatchCommandAck(deviceCode, payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed handling MQTT message on {Topic}", topic);
        }
    }

    /// <summary>
    /// IOT3-14 — tra <see cref="Domain.Entities.IotDevice"/> từ PHÂN ĐOẠN TOPIC, không phải từ <c>DeviceCode</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thiết bị publish lên <c>solar/{username}/...</c> vì ACL Mosquitto dùng <c>pattern write solar/%u/...</c>
    /// với <c>%u</c> = username = <c>deviceCode.ToLowerInvariant()</c>. Nhưng <c>IotDevice.DeviceCode</c>
    /// lưu UPPERCASE. So sánh <c>d.DeviceCode == deviceCode</c> nên KHÔNG BAO GIỜ khớp trên Postgres
    /// (so chuỗi phân biệt hoa/thường) ⇒ mọi telemetry/heartbeat/LWT qua MQTT bị bỏ với log
    /// "unknown device", trong khi thiết bị vẫn báo publish thành công (QoS 0).
    /// </para>
    /// <para>
    /// Khớp theo <c>MqttUsername</c> — giá trị ĐÃ LƯU, không phải giá trị suy ra. Nhánh dự phòng
    /// <c>MqttUsername == null</c> giữ cho thiết bị tạo trước #IoT2-26 (chưa có credential MQTT)
    /// vẫn tra được; những thiết bị đó dù sao cũng chưa nối broker nên nhánh này gần như không chạy.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Đọc trường <c>status</c> của ack. Trả <c>null</c> khi payload không phải JSON object hoặc
    /// thiếu trường — khi đó caller coi như bình thường, vì không có cơ sở để kết luận là hỏng.
    /// </summary>
    private static string? TryReadAckStatus(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("status", out var s)
                   && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// IOT3-106/M2 — cắt payload trước khi đưa vào log.
    /// </summary>
    /// <remarks>
    /// `MQTT_MAX_PACKET_SIZE` là 4096 byte; ghi nguyên payload vào log mỗi lần một thiết bị gửi sai
    /// định dạng sẽ làm log phình rất nhanh — mà thiết bị sai định dạng thì gửi sai LIÊN TỤC.
    /// 512 ký tự đủ để nhìn ra tên trường bị đặt nhầm, tức đủ để chẩn đoán.
    /// </remarks>
    private static string Truncate(string? payload, int max = 512)
    {
        if (string.IsNullOrEmpty(payload)) return string.Empty;
        return payload.Length <= max ? payload : payload[..max] + "…(cắt bớt)";
    }

    private static IQueryable<Domain.Entities.IotDevice> WhereTopicSegment(
        IQueryable<Domain.Entities.IotDevice> query, string topicSegment)
    {
        var seg = (topicSegment ?? string.Empty).Trim().ToLowerInvariant();
        return query.Where(d => !d.IsDeleted
                                && (d.MqttUsername == seg
                                    || (d.MqttUsername == null && d.DeviceCode.ToLower() == seg)));
    }

    private async Task DispatchTelemetryAsync(string deviceCode, string batterySerial, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var device = await WhereTopicSegment(unitOfWork.IotDevices.GetAllAsync(), deviceCode)
            .FirstOrDefaultAsync();
        if (device is null)
        {
            _logger.LogWarning("MQTT telemetry from unknown device {DeviceCode}", deviceCode);
            return;
        }

        var cmd = JsonSerializer.Deserialize<BatchIngestSensorReadingsCommand>(payload, JsonOptions);
        if (cmd is null)
        {
            _logger.LogWarning(
                "MQTT telemetry từ {DeviceCode}: payload không giải được thành JSON object. Payload: {Payload}",
                device.DeviceCode, Truncate(payload));
            return;
        }

        // IOT3-106/M2 — payload giải được nhưng KHÔNG có mục nào.
        //
        // `System.Text.Json` bỏ qua trường lạ, nên payload đặt sai tên mảng (ví dụ `readings` thay
        // vì `items`) sẽ deserialize THÀNH CÔNG với `Items` rỗng: không ngoại lệ, không log, không
        // bản ghi nào vào DB. Firmware báo publish OK (QoS 0), broker chuyển tin OK, cầu nối chạy
        // OK — chỉ có dữ liệu là không tồn tại. Kiểu thất bại tệ nhất, và đã tốn 15 phút truy vết
        // ngay trong buổi kiểm thử end-to-end đầu tiên.
        if (cmd.Items.Count == 0)
        {
            _logger.LogWarning(
                "MQTT telemetry từ {DeviceCode} KHÔNG có mục nào — nhiều khả năng payload sai tên "
                + "trường. Mảng phải tên `items` (không phải `readings`). Payload: {Payload}",
                device.DeviceCode, Truncate(payload));
            return;
        }
        // IOT3-14 — truyền DeviceCode CHUẨN từ DB (UPPERCASE), không phải phân đoạn topic
        // (chữ thường). Nhờ vậy handler phía sau thấy đúng một dạng chuỗi cho cả hai đường
        // vào (MQTT và HTTPS), tránh bản ghi idempotency tách làm hai theo kiểu chữ.
        cmd.DeviceCode = device.DeviceCode;
        cmd.AuthenticatedDeviceId = device.Id;

        // Inject batterySerial cho item nào còn null/empty (topic-level binding).
        foreach (var item in cmd.Items)
        {
            if (item.BatteryAssetId == Guid.Empty && string.IsNullOrWhiteSpace(item.BatteryAssetSerial))
                item.BatteryAssetSerial = batterySerial;
        }

        await mediator.Send(cmd);
    }

    private async Task DispatchHeartbeatAsync(string deviceCode, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var device = await WhereTopicSegment(unitOfWork.IotDevices.GetAllAsync(), deviceCode)
            .FirstOrDefaultAsync();
        if (device is null)
            return;

        // IOT3-106/M2 — cùng lý do với telemetry: `null` ở đây nghĩa là payload không phải JSON
        // object, và im lặng bỏ qua sẽ giấu mất một thiết bị đang gửi sai định dạng suốt nhiều ngày.
        var cmd = JsonSerializer.Deserialize<IotDeviceHeartbeatCommand>(payload, JsonOptions);
        if (cmd is null)
        {
            _logger.LogWarning(
                "MQTT heartbeat từ {DeviceCode}: payload không giải được thành JSON object. Payload: {Payload}",
                device.DeviceCode, Truncate(payload));
            return;
        }
        cmd.DeviceId = device.Id;
        cmd.DeviceCode = device.DeviceCode;   // IOT3-14 — dạng chuẩn từ DB
        await mediator.Send(cmd);
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-24 — LWT handler. Payload "offline" → mark device offline ngay,
    /// publish <c>IotDeviceWentOfflineEvent</c> + tạo Alert(DeviceOffline) cho mỗi battery.
    /// </summary>
    private async Task DispatchStatusAsync(string deviceCode, string payload)
    {
        if (!payload.Contains("offline", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var device = await WhereTopicSegment(
                unitOfWork.IotDevices.GetAllAsync().Include(d => d.Site), deviceCode)
            .FirstOrDefaultAsync();
        if (device is null)
            return;

        if (device.Status != IotDeviceStatusEnum.Active)
            return;

        var now = DateTime.UtcNow;
        device.Status = IotDeviceStatusEnum.Offline;
        device.LastOfflineAt = now;
        unitOfWork.IotDevices.UpdateAsync(device);

        // Tạo Alert(DeviceOffline) cho mỗi battery liên kết qua site.
        var siteAssets = await unitOfWork.BatteryAssets.GetAllAsync()
            .Where(a => !a.IsDeleted && a.SiteId == device.SiteId)
            .ToListAsync();

        foreach (var asset in siteAssets)
        {
            await unitOfWork.Alerts.AddAsync(new Domain.Entities.Alert
            {
                // BaseEntity.Id KHÔNG có giá trị khởi tạo (`public Guid Id { get; set; }`), nên bỏ
                // trống thì mọi Alert dựng trong vòng lặp đều mang Guid.Empty. EF gom chúng vào
                // cùng một khoá trong identity map và ném ngay ở lần AddAsync thứ hai:
                //   "The instance of entity type 'Alert' cannot be tracked because another
                //    instance with the same key value for {'Id'} is already being tracked."
                // Site chỉ có 1 asset thì không lộ; từ 2 asset trở lên là hỏng — đường LWT chưa
                // bao giờ chạy được trên site thật. Bắt được khi test MQTT E2E 31/07/2026
                // (site của GW-ESP32-MVP-001 có 5 asset).
                // Cùng khuôn với IotDeviceOfflineDetectionService (đường phát hiện offline bằng
                // polling) — chỗ đó set Id tường minh từ đầu.
                Id = Guid.NewGuid(),
                BatteryAssetId = asset.Id,
                SiteId = asset.SiteId,
                AnomalyType = AnomalyTypeEnum.DeviceOffline,
                Severity = AlertSeverityEnum.Warning,
                DetectedAt = now,
                Status = AlertStatusEnum.Open,
                DedupWindowEndUtc = now.AddHours(1)
            });
        }

        // Publish outbox event — NotificationService consume riêng cho Staff/ops.
        var outboxWriter = scope.ServiceProvider.GetService<SharedContracts.Interfaces.IIntegrationEventOutboxWriter>();
        if (outboxWriter is not null)
        {
            await outboxWriter.WriteAsync(new SharedContracts.Events.IotDeviceWentOfflineEvent(
                IotDeviceId: device.Id,
                DeviceCode: device.DeviceCode,
                DisplayName: device.DisplayName,
                SiteId: device.SiteId,
                SiteName: device.Site?.Name,
                LastSeenAt: device.LastSeenAt ?? now,
                DetectedAt: now,
                OfflineDurationSeconds: 0,
                AffectedBatteryCount: siteAssets.Count,
                AlertId: null));
        }

        await unitOfWork.SaveChangesAsync();
        _logger.LogWarning("Device {DeviceCode} marked offline via LWT — {AssetCount} assets alerted", deviceCode, siteAssets.Count);
    }

    private void DispatchCommandAck(string deviceCode, string payload)
    {
        // Sprint IoT-2 #IoT2-25 — log ack để admin trace.
        // Payload kỳ vọng: {"cmdId":"...","status":"ok"|"failed","error":"..."}
        // Thiết bị trả `status`: "ok" | "failed" | "rejected" | "unknown"
        // (`iot/firmware-esp32/src/cmd/command_handler.cpp`).
        //
        // Ghi TẤT CẢ ở mức Information như trước là chôn mất ba trạng thái hỏng giữa hàng nghìn
        // dòng "ok". Đúng lỗi đã xảy ra: dropdown frontend liệt kê 5 loại lệnh mà firmware KHÔNG
        // hiểu loại nào; mọi lệnh gửi đi đều ack "unknown" và không ai nhận ra suốt nhiều tuần,
        // vì backend trả 202 và toast báo thành công.
        var status = TryReadAckStatus(payload);
        if (status is "ok" or null)
        {
            _logger.LogInformation("MQTT cmd/ack from {DeviceCode}: {Payload}", deviceCode, payload);
            return;
        }

        _logger.LogWarning(
            "MQTT cmd/ack từ {DeviceCode} báo status={Status} — lệnh KHÔNG được thực thi. "
            + "`unknown` nghĩa là firmware không hiểu loại lệnh (chỉ hiểu set_interval / "
            + "trigger_ota / request_heartbeat). Payload: {Payload}",
            deviceCode, status, Truncate(payload));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
