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
/// Sprint IoT-1 (#253) — long-running connection tới Mosquitto/EMQX.
/// Subscribe wildcard <c>telemetry/+</c>, <c>heartbeat/+</c>, <c>status/+</c>.
/// Mỗi message dispatch sang handler tương ứng:
/// - telemetry → <see cref="BatchIngestSensorReadingsCommand"/>.
/// - heartbeat → <see cref="IotDeviceHeartbeatCommand"/>.
/// - status (LWT "offline") → mark device offline + alert.
///
/// Implement cả <see cref="IMqttBridgePublisher"/> để publish downlink cmd.
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
            _logger.LogInformation("MQTT bridge connected to {Host}:{Port}", _options.Host, _options.Port);
            await _client.SubscribeAsync(MqttTopicMap.TelemetryWildcard);
            await _client.SubscribeAsync(MqttTopicMap.HeartbeatWildcard);
            await _client.SubscribeAsync(MqttTopicMap.StatusWildcard);
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

    public async Task PublishCommandAsync(string deviceCode, string payloadJson, CancellationToken ct = default)
    {
        if (_client is null || !_client.IsStarted)
        {
            _logger.LogWarning("Cannot publish cmd — MQTT bridge not started.");
            return;
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
        if (!MqttTopicMap.TryExtractDeviceCode(topic, out var deviceCode))
            return;

        try
        {
            if (topic.StartsWith("telemetry/", StringComparison.Ordinal))
                await DispatchTelemetryAsync(deviceCode, payload);
            else if (topic.StartsWith("heartbeat/", StringComparison.Ordinal))
                await DispatchHeartbeatAsync(deviceCode, payload);
            else if (topic.StartsWith("status/", StringComparison.Ordinal))
                await DispatchStatusAsync(deviceCode, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed handling MQTT message on {Topic}", topic);
        }
    }

    private async Task DispatchTelemetryAsync(string deviceCode, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<IIotApiKeyService>();
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

    private async Task DispatchStatusAsync(string deviceCode, string payload)
    {
        if (!payload.Contains("offline", StringComparison.OrdinalIgnoreCase))
            return;

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
        var device = await unitOfWork.IotDevices.GetAllAsync()
            .FirstOrDefaultAsync(d => d.DeviceCode == deviceCode && !d.IsDeleted);
        if (device is null)
            return;

        if (device.Status == IotDeviceStatusEnum.Active)
        {
            device.Status = IotDeviceStatusEnum.Offline;
            device.LastOfflineAt = DateTime.UtcNow;
            unitOfWork.IotDevices.UpdateAsync(device);
            await unitOfWork.SaveChangesAsync();
            _logger.LogWarning("Device {DeviceCode} marked offline via LWT", deviceCode);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
