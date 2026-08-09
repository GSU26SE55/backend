using System.Text;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mqtt;
using BatteryService.Infrastructure.Implements.Services;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Moq;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace BatteryService.IntegrationTests.Mqtt;

/// <summary>
/// GH-784 — chứng minh credential do BACKEND sinh ra được BROKER THẬT chấp nhận.
///
/// <para>
/// Vì sao không thể dừng ở unit test: cả chuỗi lỗi của issue này (hash sai định dạng, broker host
/// null, lệch chữ hoa/thường giữa topic và ACL) đều thuộc loại mà <b>mọi tầng đều báo thành công</b>
/// — API trả 201, DB có bản ghi, log sạch — rồi thiết bị nhận "Connection Refused: not authorised".
/// Chỉ có một cách chứng minh: cầm đúng credential API cấp, nối vào Mosquitto thật.
/// </para>
/// <para>
/// Test này KHÔNG dùng <c>mosquitto_passwd</c> để sinh file (như
/// <see cref="MosquittoBrokerFixture"/> làm) — nó dùng chính <c>IotApiKeyService</c> và
/// <c>MosquittoPasswordFile.Compose</c> của production. Nếu định dạng hash sai một chi tiết
/// (SHA256 thay vì SHA512, tiền tố <c>PBKDF2$</c> thay vì <c>$7$</c>, hash 32 byte thay vì 64),
/// broker sẽ từ chối và test đỏ.
/// </para>
/// </summary>
public sealed class GeneratedCredentialAcceptedByBrokerTests : IAsyncLifetime
{
    private const string BridgeUser = "backend-bridge";
    private const string BridgePassword = "bridge-pw-for-test";

    private IContainer _container = null!;
    private string _tempDir = null!;
    private string _deviceUser = null!;
    private string _devicePassword = null!;

    private string Host => _container.Hostname;
    private int Port => _container.GetMappedPublicPort(1883);

    private static string RepoMqttConfigDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Không tìm thấy gốc repo từ " + AppContext.BaseDirectory);
            return Path.Combine(dir!.FullName, "infra", "mqtt", "mosquitto", "config");
        }
    }

    public async Task InitializeAsync()
    {
        // Sinh credential không đụng DB — chỉ cần một UoW giả để dựng service.
        var apiKeys = new IotApiKeyService(new Mock<IBatteryUnitOfWork>().Object);

        // Credential thiết bị: sinh bằng ĐÚNG code production.
        var deviceCred = apiKeys.GenerateMqttCredential("GW-TEST-784");
        _deviceUser = deviceCred.Username;
        _devicePassword = deviceCred.RawPassword;

        // Bridge cũng dùng cùng bộ sinh — bảo đảm cả hai đường đều qua được định dạng này.
        var bridgeHashOnly = apiKeys.GenerateMqttCredential(BridgeUser);

        // Dựng file passwd bằng chính hàm production, KHÔNG dùng mosquitto_passwd.
        var passwd = MosquittoPasswordFile.Compose(
            existingContent: $"{BridgeUser}:{HashFor(apiKeys, BridgePassword, bridgeHashOnly)}\n",
            devices: new[] { new MosquittoCredential(_deviceUser, deviceCred.PasswordHash) });

        _tempDir = Directory.CreateTempSubdirectory("gh784-").FullName;
        var passwdPath = Path.Combine(_tempDir, "passwd");
        await File.WriteAllTextAsync(passwdPath, passwd);

        var cfgDir = RepoMqttConfigDir;
        var bootstrap = string.Join(" && ",
            // IOT3-106/M1 — `password_file` của repo nay là `/mosquitto/config-src/passwd`
            // (đường của một THƯ MỤC được mount) thay vì `/mosquitto/config/passwd` (file lẻ).
            // Test nạp CHÍNH conf của repo nên phải sinh passwd vào đúng đường đó.
            "mkdir -p /mosquitto/config/conf.d /mosquitto/config-src",
            "cp /mosquitto/config/passwd /mosquitto/config-src/passwd",
            // Broker tụt quyền sang user `mosquitto`; thiếu chown là chết ngay với
            // "Unable to open pwfile". Mosquitto 2.0 cũng từ chối file world-readable.
            "chown mosquitto:mosquitto /mosquitto/config-src/passwd",
            "chmod 0700 /mosquitto/config-src/passwd",
            "exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf");

        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2.0")
            .WithResourceMapping(Path.Combine(cfgDir, "mosquitto.conf"), "/mosquitto/config/")
            .WithResourceMapping(Path.Combine(cfgDir, "acl.conf"), "/mosquitto/config/")
            .WithResourceMapping(passwdPath, "/mosquitto/config/")
            .WithPortBinding(1883, true)
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand(bootstrap)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1883))
            .Build();

        await _container.StartAsync();
    }

    /// <summary>
    /// Sinh hash `$7$` cho một mật khẩu CHO TRƯỚC bằng chính thuật toán production.
    /// </summary>
    /// <remarks>
    /// <c>GenerateMqttCredential</c> tự sinh mật khẩu ngẫu nhiên, nên với bridge (mật khẩu cố định)
    /// phải tái tạo hash theo đúng tham số đọc từ một bản ghi mẫu — cách này vẫn đi qua đúng định
    /// dạng đang được kiểm chứng.
    /// </remarks>
    private static string HashFor(IotApiKeyService _, string password, GeneratedMqttCredential sample)
    {
        var parts = sample.PasswordHash.Split('$');
        var iterations = int.Parse(parts[2]);
        var salt = Convert.FromBase64String(parts[3]);
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA512, 64);
        return $"$7${iterations}${parts[3]}${Convert.ToBase64String(hash)}";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        if (_tempDir is not null && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<MqttClientConnectResult> TryConnectAsync(string user, string password)
    {
        var client = new MqttFactory().CreateMqttClient();
        try
        {
            return await client.ConnectAsync(new MqttClientOptionsBuilder()
                .WithClientId($"gh784-{Guid.NewGuid():N}")
                .WithTcpServer(Host, Port)
                .WithCredentials(user, password)
                .Build());
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task BackendGeneratedCredential_IsAcceptedByARealBroker()
    {
        // Đây là bằng chứng mà issue đòi: credential API cấp phải connect được thật.
        var result = await TryConnectAsync(_deviceUser, _devicePassword);

        result.ResultCode.Should().Be(MqttClientConnectResultCode.Success,
            "định dạng hash sai một chi tiết là broker trả 'not authorised' — đúng lỗi GH-784");
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        // Chống test vô nghĩa: nếu broker nhận mọi mật khẩu thì test trên chẳng chứng minh gì.
        var act = async () => await TryConnectAsync(_deviceUser, _devicePassword + "x");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DeviceCanPublishToItsOwnLowercaseTopic()
    {
        // Xung đột case của GH-784: ACL dùng `solar/%u/...` với %u = username chữ thường.
        var client = new MqttFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"gh784-pub-{Guid.NewGuid():N}")
            .WithTcpServer(Host, Port)
            .WithCredentials(_deviceUser, _devicePassword)
            .Build());

        var result = await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic($"solar/{_deviceUser}/BAT-1/telemetry")
            .WithPayload("{}")
            .Build());

        result.IsSuccess.Should().BeTrue();
        await client.DisconnectAsync();
        client.Dispose();
    }

    [Fact]
    public async Task DeviceCannotPublishToAnotherDevicesTopic()
    {
        // ACL phải thực sự cách ly. Không có khẳng định này thì "publish được" ở test trên có thể
        // chỉ nghĩa là broker cho mọi người ghi mọi nơi.
        //
        // Cách chứng minh: quan sát message nào THỰC SỰ đi qua broker, chứ KHÔNG dựa vào việc bị
        // ngắt kết nối — Mosquitto lặng lẽ bỏ message QoS 0 bị ACL cấm mà không đóng kết nối, nên
        // "vẫn còn kết nối" không nói lên điều gì.
        var received = new List<string>();
        var observer = new MqttFactory().CreateMqttClient();
        observer.ApplicationMessageReceivedAsync += e =>
        {
            lock (received)
                received.Add(e.ApplicationMessage.Topic);
            return Task.CompletedTask;
        };
        await observer.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"gh784-obs-{Guid.NewGuid():N}")
            .WithTcpServer(Host, Port)
            .WithCredentials(BridgeUser, BridgePassword)
            .Build());
        await observer.SubscribeAsync("solar/#");

        var device = new MqttFactory().CreateMqttClient();
        await device.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId($"gh784-dev-{Guid.NewGuid():N}")
            .WithTcpServer(Host, Port)
            .WithCredentials(_deviceUser, _devicePassword)
            .Build());

        var ownTopic = $"solar/{_deviceUser}/BAT-OWN/telemetry";
        const string foreignTopic = "solar/gw-someone-else/BAT-VICTIM/telemetry";

        await device.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(ownTopic).WithPayload("{}").Build());
        await device.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(foreignTopic).WithPayload("{}").Build());

        // Chờ topic hợp lệ tới — cùng khoảng thời gian đó cũng đủ rộng cho topic bị chặn nếu nó lọt.
        var arrived = await WaitUntilAsync(() =>
        {
            lock (received)
                return received.Contains(ownTopic);
        }, TimeSpan.FromSeconds(10));

        await device.DisconnectAsync();
        await observer.DisconnectAsync();
        device.Dispose();
        observer.Dispose();

        arrived.Should().BeTrue("ACL phải cho thiết bị ghi topic của CHÍNH nó");
        lock (received)
        {
            received.Should().NotContain(foreignTopic,
                "một gateway bị chiếm quyền không được bơm số liệu giả cho pin của site khác");
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(100);
        }
        return condition();
    }
}
