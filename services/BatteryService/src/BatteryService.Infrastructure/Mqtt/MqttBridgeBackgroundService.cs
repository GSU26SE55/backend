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
            throw new InvalidOperationException("MQTT bridge is not started (Mqtt:Enabled=false or broker not reachable).");
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
                    await DispatchStatusAsync(deviceCode, payload, e.ApplicationMessage.Retain);
                    break;
                case "cmd_ack":
                    await DispatchCommandAckAsync(deviceCode, payload);
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
        if (string.IsNullOrEmpty(payload))
            return string.Empty;
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
    /// LWT/status handler. A live offline delivery transitions immediately; a retained replay
    /// keeps the heartbeat freshness guard because it may be stale. Payload matching is exact to
    /// avoid treating arbitrary strings containing "offline" as a state transition.
    /// </summary>
    private async Task DispatchStatusAsync(string deviceCode, string payload, bool isRetained)
    {
        var normalized = payload.Trim().ToLowerInvariant();
        if (normalized is not ("online" or "offline"))
        {
            _logger.LogWarning(
                "Ignore invalid MQTT status payload from {DeviceCode}: {Payload}",
                deviceCode,
                payload);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var device = await WhereTopicSegment(
                unitOfWork.IotDevices.GetAllAsync().Include(d => d.Site), deviceCode)
            .FirstOrDefaultAsync();
        if (device is null)
        {
            _logger.LogWarning("MQTT status from unknown device {DeviceCode}", deviceCode);
            return;
        }

        if (normalized == "online")
        {
            if (device.Status is IotDeviceStatusEnum.Disabled or IotDeviceStatusEnum.Decommissioned)
                return;

            var availability = scope.ServiceProvider.GetRequiredService<IIotDeviceAvailabilityService>();
            var recovery = await availability.RecordHealthySignalAsync(
                device,
                DateTime.UtcNow,
                forceActivation: false);
            unitOfWork.IotDevices.UpdateAsync(device);
            await unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "Accepted MQTT online signal for {DeviceCode} (retained={Retained}, recovered={Recovered})",
                deviceCode,
                isRetained,
                recovery.BecameActive);
            return;
        }

        var detector = scope.ServiceProvider.GetRequiredService<IIotDeviceOfflineDetectionService>();
        // MQTT sets Retain=false for a live delivery, even when the Will itself is stored as
        // retained. That live LWT is direct broker evidence that the socket died, so waiting an
        // additional heartbeat window made power loss take 5-7 minutes to appear. A retained
        // replay after the bridge subscribes is different: it can be stale and must keep the
        // freshness guard.
        var transition = isRetained
            ? await detector.TryMarkOfflineAsync(
                device.Id,
                DateTime.UtcNow,
                Math.Max(15, _options.LwtOfflineGraceSeconds),
                CancellationToken.None)
            : await detector.TryMarkOfflineFromLwtAsync(
                device.Id,
                DateTime.UtcNow,
                CancellationToken.None);

        if (transition.MarkedOffline)
        {
            _logger.LogWarning(
                "Device {DeviceCode} marked offline via MQTT LWT (retained={Retained}, eventQueued={EventQueued})",
                deviceCode,
                isRetained,
                transition.EventQueued);
        }
        else
        {
            _logger.LogInformation(
                "Ignored MQTT offline signal for {DeviceCode}: device is not Active or is still fresh (retained={Retained})",
                deviceCode,
                isRetained);
        }
    }

    private async Task DispatchCommandAckAsync(string deviceCode, string payload)
    {
        await PersistCommandAckAsync(deviceCode, payload);
        // Sprint IoT-2 #IoT2-25 — log acknowledgements for admin tracing.
        // Expected payload: {"cmdId":"...","status":"ok"|"failed","error":"..."}
        // Device status values: "ok" | "failed" | "rejected" | "unknown"
        // (`iot/firmware-esp32/src/cmd/command_handler.cpp`).
        //
        // Logging every acknowledgement at Information would bury failures among thousands of
        // successful entries. Unknown commands must remain visible because a 202 response only
        // confirms dispatch; it does not confirm device execution.
        var status = TryReadAckStatus(payload);
        if (status is "ok" or null)
        {
            _logger.LogInformation("MQTT cmd/ack from {DeviceCode}: {Payload}", deviceCode, payload);
            return;
        }

        _logger.LogWarning(
            "MQTT cmd/ack from {DeviceCode} reported status={Status}; the command was not executed. "
            + "`unknown` means the firmware does not support the command type (supported: set_interval / "
            + "trigger_ota / request_heartbeat / set_bms_switch). Payload: {Payload}",
            deviceCode, status, Truncate(payload));
    }

    internal async Task PersistCommandAckAsync(string deviceCode, string payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("cmdId", out var cmdIdValue)
                || cmdIdValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(cmdIdValue.GetString())
                || !root.TryGetProperty("status", out var statusValue)
                || statusValue.ValueKind != JsonValueKind.String)
                return;

            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
            var device = await WhereTopicSegment(unitOfWork.IotDevices.GetAllAsync(), deviceCode)
                .AsNoTracking()
                .FirstOrDefaultAsync();
            if (device is null)
                return;

            var cmdId = cmdIdValue.GetString()!;
            var command = await unitOfWork.IotDeviceCommands.GetAllAsync()
                .FirstOrDefaultAsync(item => !item.IsDeleted
                                             && item.CmdId == cmdId
                                             && item.IotDeviceId == device.Id);
            if (command is null)
                return;

            var status = statusValue.GetString()?.Trim().ToLowerInvariant();
            command.Status = status switch
            {
                "ok" => IotDeviceCommandStatusEnum.Ok,
                "failed" => IotDeviceCommandStatusEnum.Failed,
                "rejected" => IotDeviceCommandStatusEnum.Rejected,
                _ => IotDeviceCommandStatusEnum.Unknown
            };
            // Lưu lý do THÔ của firmware. Trước đây `set_bms_switch` bị chuẩn hoá NGAY Ở ĐÂY rồi
            // ghi đè, nên lý do gốc mất vĩnh viễn trong DB — không tầng nào phía sau khôi phục
            // được. Hệ quả cụ thể: mobile dò chuỗi "unsupported"/"not support" để ẩn control trên
            // thiết bị không hỗ trợ, nhưng chuỗi tới nơi luôn là câu chuẩn hoá không chứa từ khoá
            // nào, nên nhánh đó chưa từng chạy.
            //
            // Chuẩn hoá là việc của tầng ĐỌC: `GetBmsSwitchStateQueryHandler.NormalizeCommandError`
            // đã làm đúng việc đó (và còn phủ thêm `TimedOut`), rồi trả kèm `DeviceReason` thô cho
            // client nào cần biết lý do thật.
            command.AckError = root.TryGetProperty("error", out var errorValue)
                               && errorValue.ValueKind == JsonValueKind.String
                ? errorValue.GetString()
                : null;
            command.ResultJson = root.TryGetProperty("state", out var stateValue)
                                 && stateValue.ValueKind == JsonValueKind.Object
                ? stateValue.GetRawText()
                : null;
            command.AckedAt = DateTime.UtcNow;
            unitOfWork.IotDeviceCommands.UpdateAsync(command);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
