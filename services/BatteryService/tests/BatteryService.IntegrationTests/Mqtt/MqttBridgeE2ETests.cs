using System.Text;
using System.Text.Json;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Mqtt;
using BatteryService.Infrastructure.Persistence;
using BatteryService.Infrastructure.Implements.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MQTTnet;
using MQTTnet.Client;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace BatteryService.IntegrationTests.Mqtt;

/// <summary>
/// Sprint IoT-1 <c>#253</c> — 3 kịch bản MQTT mà task yêu cầu, chạy trên broker Mosquitto THẬT:
/// <list type="number">
///   <item>telemetry qua broker đi đúng <see cref="BatchIngestSensorReadingsCommand"/>;</item>
///   <item>LWT <c>offline</c> → device Offline + Alert(DeviceOffline) cho mọi pin của site;</item>
///   <item>ACL chặn device lạ ghi đè topic của device khác.</item>
/// </list>
/// Trước đây nhóm này chỉ có <c>MqttTopicMapTests</c> (thuần map chuỗi) — không kịch bản nào chạm
/// tới broker, nên cả đường bridge lẫn ACL đều chưa từng được kiểm chứng.
/// </summary>
[Collection(nameof(MosquittoCollection))]
public class MqttBridgeE2ETests
{
    private readonly MosquittoBrokerFixture _broker;

    public MqttBridgeE2ETests(MosquittoBrokerFixture broker) => _broker = broker;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Context mới trên CÙNG một InMemory database (đặt tên qua <paramref name="dbName"/>).
    ///
    /// Bắt buộc phải tách instance: bridge xử lý message trên thread của MQTT client, trong khi test
    /// poll kết quả trên thread test. Dùng chung 1 <see cref="ApplicationDbContext"/> là dính
    /// "A second operation was started on this context instance" — EF DbContext không thread-safe.
    /// Tách ra cũng đúng với runtime: mỗi message tạo 1 scope DI ⇒ 1 DbContext riêng.
    /// </summary>
    private static ApplicationDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new AuditableEntityInterceptor(new CurrentUserService(new HttpContextAccessor())));

    /// <summary>
    /// Bridge thật + scope factory trỏ vào DbContext của test. <paramref name="mediator"/> để test
    /// bắt được command mà bridge gửi đi.
    /// </summary>
    private static (MqttBridgeBackgroundService Bridge, ServiceProvider Provider) BuildBridge(
        string dbName, IMediator mediator, string host, int port, string user)
    {
        var services = new ServiceCollection();
        // Scoped + context MỚI mỗi scope — giống hệt runtime, và tránh đụng context của test.
        services.AddScoped<IBatteryUnitOfWork>(_ => new UnitOfWork(NewDb(dbName)));
        services.AddSingleton(mediator);
        var provider = services.BuildServiceProvider();

        var options = Options.Create(new MqttOptions
        {
            Enabled = true,
            Host = host,
            Port = port,
            UseTls = false,
            Username = user,
            Password = MosquittoBrokerFixture.Password,
            ClientId = $"bridge-{Guid.NewGuid():N}",
            ReconnectIntervalSeconds = 1
        });

        var bridge = new MqttBridgeBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<MqttBridgeBackgroundService>.Instance);

        return (bridge, provider);
    }

    private async Task<IMqttClient> ConnectAsync(string user)
    {
        var client = new MqttFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"test-{user}-{Guid.NewGuid():N}")
            .WithTcpServer(_broker.Host, _broker.Port)
            .WithCredentials(user, MosquittoBrokerFixture.Password)
            .Build());
        return client;
    }

    /// <summary>Chờ tới khi <paramref name="condition"/> đúng, tối đa <paramref name="timeout"/>.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    // ---------------------------------------------------------------- 1) telemetry → ingest

    [Fact]
    public async Task Telemetry_PublishedThroughBroker_ReachesIngestCommand()
    {
        var dbName = $"mqtt-e2e-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var siteId = Guid.NewGuid();
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = MosquittoBrokerFixture.DeviceA,
            DisplayName = "GW test A",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active
        };
        db.IotDevices.Add(device);
        await db.SaveChangesAsync();

        BatchIngestSensorReadingsCommand? captured = null;
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<BatchIngestSensorReadingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<CommonResponse<SensorReadingBatchIngestResult>> c, CancellationToken _) =>
                captured = (BatchIngestSensorReadingsCommand)c)
            .ReturnsAsync(new CommonResponse<SensorReadingBatchIngestResult>());

        var (bridge, provider) = BuildBridge(dbName, mediator.Object, _broker.Host, _broker.Port,
            MosquittoBrokerFixture.BridgeUser);
        await using (provider)
        {
            await bridge.StartAsync(CancellationToken.None);
            // Managed client nối + subscribe bất đồng bộ — chờ tới khi thật sự có subscription.
            await Task.Delay(1500);

            var publisher = await ConnectAsync(MosquittoBrokerFixture.BridgeUser);
            var payload = JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new
                    {
                        time = DateTime.UtcNow,
                        voltage = 51.2m,
                        current = 3.4m,
                        temperature = 30.1m,
                        socPercent = 88.5m
                    }
                }
            }, Json);

            await publisher.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(MqttTopicMap.Telemetry(MosquittoBrokerFixture.DeviceA, "BAT-SERIAL-1"))
                .WithPayload(payload)
                .Build());

            (await WaitUntilAsync(() => captured is not null, TimeSpan.FromSeconds(15)))
                .Should().BeTrue("bridge phải nhận telemetry qua broker và gửi ingest command");

            await publisher.DisconnectAsync();
            await bridge.StopAsync(CancellationToken.None);
        }

        captured!.DeviceCode.Should().Be(MosquittoBrokerFixture.DeviceA);
        captured.AuthenticatedDeviceId.Should().Be(device.Id,
            "bridge phải resolve deviceCode → IotDevice.Id trước khi gửi command");
        captured.Items.Should().ContainSingle();
        // batterySerial nằm ở segment topic, KHÔNG ở payload — bridge phải bơm xuống item.
        captured.Items[0].BatteryAssetSerial.Should().Be("BAT-SERIAL-1");
        captured.Items[0].Voltage.Should().Be(51.2m);
    }

    // ---------------------------------------------------------------- 2) LWT → Offline + Alert

    [Fact]
    public async Task LastWill_OfflinePayload_MarksDeviceOffline_AndAlertsEveryAssetOfSite()
    {
        var dbName = $"mqtt-e2e-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var siteId = Guid.NewGuid();
        db.Sites.Add(new Site { Id = siteId, Name = "Site LWT", Address = "addr" });
        var device = new IotDevice
        {
            Id = Guid.NewGuid(),
            DeviceCode = MosquittoBrokerFixture.DeviceB,
            DisplayName = "GW test B",
            SiteId = siteId,
            Status = IotDeviceStatusEnum.Active,
            LastSeenAt = DateTime.UtcNow.AddMinutes(-1)
        };
        db.IotDevices.Add(device);
        // 2 pin — đủ để lộ lỗi Guid.Empty trùng khoá nếu Alert không set Id tường minh.
        db.BatteryAssets.Add(new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A-1", SiteId = siteId });
        db.BatteryAssets.Add(new BatteryAsset { Id = Guid.NewGuid(), SerialNumber = "A-2", SiteId = siteId });
        await db.SaveChangesAsync();

        var (bridge, provider) = BuildBridge(dbName, Mock.Of<IMediator>(), _broker.Host, _broker.Port,
            MosquittoBrokerFixture.BridgeUser);
        await using (provider)
        {
            await bridge.StartAsync(CancellationToken.None);
            await Task.Delay(1500);

            var publisher = await ConnectAsync(MosquittoBrokerFixture.BridgeUser);
            await publisher.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(MqttTopicMap.Status(MosquittoBrokerFixture.DeviceB))
                .WithPayload("offline")
                .Build());

            var ok = await WaitUntilAsync(
                () =>
                {
                    using var probe = NewDb(dbName);
                    return probe.Alerts.AsNoTracking()
                        .Count(a => a.AnomalyType == AnomalyTypeEnum.DeviceOffline) == 2;
                },
                TimeSpan.FromSeconds(20));
            ok.Should().BeTrue("LWT offline phải sinh 1 Alert(DeviceOffline) cho MỖI pin của site");

            await publisher.DisconnectAsync();
            await bridge.StopAsync(CancellationToken.None);
        }

        await using var verify = NewDb(dbName);
        var saved = await verify.IotDevices.AsNoTracking().FirstAsync(d => d.Id == device.Id);
        saved.Status.Should().Be(IotDeviceStatusEnum.Offline);
        saved.LastOfflineAt.Should().NotBeNull();

        var alerts = await verify.Alerts.AsNoTracking().ToListAsync();
        alerts.Should().HaveCount(2);
        alerts.Select(a => a.Id).Distinct().Should().HaveCount(2,
            "mỗi Alert phải có Id riêng — để Guid.Empty là EF ném trùng khoá ở pin thứ hai");
        alerts.Should().OnlyContain(a => a.Severity == AlertSeverityEnum.Warning
                                      && a.Status == AlertStatusEnum.Open);
    }

    // ---------------------------------------------------------------- 3) ACL chặn device lạ

    [Fact]
    public async Task Acl_DeviceCannotPublishToAnotherDeviceTopic_ButCanPublishToItsOwn()
    {
        // Bridge-user nghe toàn tree để quan sát message nào THỰC SỰ đi qua broker.
        var received = new List<string>();
        var observer = await ConnectAsync(MosquittoBrokerFixture.BridgeUser);
        observer.ApplicationMessageReceivedAsync += e =>
        {
            lock (received) received.Add(e.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };
        await observer.SubscribeAsync("solar/#");

        var deviceA = await ConnectAsync(MosquittoBrokerFixture.DeviceA);

        var ownTopic = MqttTopicMap.Telemetry(MosquittoBrokerFixture.DeviceA, "BAT-OWN");
        var foreignTopic = MqttTopicMap.Telemetry(MosquittoBrokerFixture.DeviceB, "BAT-VICTIM");

        await deviceA.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(ownTopic).WithPayload("{}").Build());
        await deviceA.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(foreignTopic).WithPayload("{}").Build());

        // Chờ topic hợp lệ tới — mốc thời gian này cũng đủ rộng cho topic bị chặn nếu nó lọt.
        var arrived = await WaitUntilAsync(() =>
        {
            lock (received) return received.Contains(ownTopic);
        }, TimeSpan.FromSeconds(10));

        await deviceA.DisconnectAsync();
        await observer.DisconnectAsync();

        arrived.Should().BeTrue(
            "ACL `pattern write solar/%u/+/telemetry` phải cho device ghi topic của CHÍNH nó");

        lock (received)
        {
            received.Should().NotContain(foreignTopic,
                "device A không được ghi lên topic của device B — nếu lọt thì một gateway bị chiếm quyền " +
                "có thể bơm số liệu giả cho pin của site khác");
        }
    }
}
