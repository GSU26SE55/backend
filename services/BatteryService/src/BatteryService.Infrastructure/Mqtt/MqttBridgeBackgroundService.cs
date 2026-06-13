using System.Text;
using System.Text.Json;
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Enums;
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
            _logger.LogInformation(
                "MQTT bridge connected to broker, 4 subscriptions ({Host}:{Port})",
                _options.Host, _options.Port);
        };
        _client.DisconnectedAsync += d =>
        {
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

    private async Task DispatchTelemetryAsync(string deviceCode, string batterySerial, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var device = await unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
        if (device is null)
        {
            _logger.LogWarning("MQTT telemetry from unknown device {DeviceCode}", deviceCode);
            return;
        }

        var cmd = JsonSerializer.Deserialize<BatchIngestSensorReadingsCommand>(payload, JsonOptions);
        if (cmd is null)
            return;
        cmd.DeviceCode = deviceCode;
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

        var device = await unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
        if (device is null)
            return;

        var cmd = JsonSerializer.Deserialize<IotDeviceHeartbeatCommand>(payload, JsonOptions);
        if (cmd is null)
            return;
        cmd.DeviceId = device.Id;
        cmd.DeviceCode = deviceCode;
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
        var device = await unitOfWork.IotDevices.GetAllAsync()
            .Include(d => d.Site)
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
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
        _logger.LogInformation("MQTT cmd/ack from {DeviceCode}: {Payload}", deviceCode, payload);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
