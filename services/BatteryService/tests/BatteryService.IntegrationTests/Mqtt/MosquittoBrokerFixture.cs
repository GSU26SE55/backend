using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BatteryService.IntegrationTests.Mqtt;

/// <summary>
/// Sprint IoT-1 <c>#253</c> — broker Mosquitto THẬT cho test MQTT.
///
/// <para><b>Vì sao không mock:</b> 3 hạng mục cần chứng minh là "telemetry <i>qua broker</i> đi đúng
/// ingest command", "LWT → Offline + alert" và "ACL chặn device lạ". Hai cái đầu có thể mock, cái
/// thứ ba thì không — ACL là hành vi của chính broker, mock đi là mất hết ý nghĩa.</para>
///
/// <para><b>Điểm mấu chốt:</b> fixture nạp <b>chính file</b> <c>infra/mqtt/mosquitto/config/acl.conf</c>
/// và <c>mosquitto.conf</c> của repo, không phải bản chép trong test. Nhờ vậy test này đồng thời là
/// hàng rào chống trôi giữa <see cref="BatteryService.Infrastructure.Mqtt.MqttTopicMap"/> và ACL:
/// đổi schema topic một bên mà quên bên kia là test đỏ.</para>
///
/// <para><b>passwd sinh lúc chạy:</b> file <c>passwd</c> thật bị gitignore (là secret), nên container
/// tự tạo bằng <c>mosquitto_passwd</c> trong entrypoint rồi mới <c>exec mosquitto</c>. Cách này cũng
/// tránh phải commit hash mật khẩu vào repo test.</para>
/// </summary>
public sealed class MosquittoBrokerFixture : IAsyncLifetime
{
    /// <summary>User bridge của backend — ACL cho <c>readwrite solar/#</c>.</summary>
    public const string BridgeUser = "backend-bridge";

    /// <summary>Device hợp lệ trong bài test ACL. Username = deviceCode lower-case (quy ước ACL).</summary>
    public const string DeviceA = "gw-test-a";

    /// <summary>Device thứ hai — dùng làm "nạn nhân" để chứng minh device A không ghi đè được.</summary>
    public const string DeviceB = "gw-test-b";

    /// <summary>
    /// IOT3-15 — <c>DeviceCode</c> đúng như <c>CreateIotDeviceCommandHandler</c> lưu vào DB:
    /// <c>Trim().ToUpperInvariant()</c>. Username MQTT vẫn là bản chữ thường ở trên.
    /// </summary>
    /// <remarks>
    /// Trước đây bài test seed thẳng <c>DeviceCode = DeviceA</c> (chữ thường), tức bỏ qua bước
    /// chuẩn hoá của handler. Vì thế nó KHÔNG THỂ phát hiện lỗi so sánh phân biệt hoa/thường ở
    /// bridge — test xanh trong khi hệ thống thật rơi toàn bộ telemetry MQTT.
    /// </remarks>
    public const string DeviceACode = "GW-TEST-A";

    /// <inheritdoc cref="DeviceACode"/>
    public const string DeviceBCode = "GW-TEST-B";

    public const string Password = "test-mqtt-pw";

    private IContainer _container = null!;

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1883);

    /// <summary>Thư mục config MQTT trong repo — nguồn sự thật cho cả runtime lẫn test.</summary>
    private static string RepoMqttConfigDir
    {
        get
        {
            // Đi ngược từ thư mục chạy test (bin/Debug/net8.0) lên gốc repo.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;

            if (dir is null)
                throw new InvalidOperationException(
                    "Không tìm thấy gốc repo (SolarBatteryMaintainance.slnx) từ " + AppContext.BaseDirectory);

            var cfg = Path.Combine(dir.FullName, "infra", "mqtt", "mosquitto", "config");
            if (!Directory.Exists(cfg))
                throw new InvalidOperationException($"Thiếu thư mục config MQTT: {cfg}");
            return cfg;
        }
    }

    public async Task InitializeAsync()
    {
        var cfgDir = RepoMqttConfigDir;

        // Entrypoint tuỳ biến: tạo passwd cho 3 user rồi mới chạy broker.
        // `mosquitto.conf` của repo có `include_dir conf.d` → thư mục phải tồn tại, nếu không
        // Mosquitto báo lỗi và thoát. Test chạy cổng 1883 plain nên không sinh cert (TLS đã được
        // nghiệm thu riêng ở infra/mqtt/README.md).
        // IOT3-106/M1 — `password_file` trong mosquitto.conf của repo nay trỏ tới
        // `/mosquitto/config-src/passwd` (đường của MỘT THƯ MỤC được mount), thay vì
        // `/mosquitto/config/passwd` (đường của một FILE LẺ). Đổi vì bind mount file lẻ không
        // truyền đủ thay đổi inode do `File.Move` tạo ra, khiến broker không bao giờ nạp lại
        // credential của thiết bị mới.
        //
        // Fixture nạp CHÍNH file conf của repo (đó là điểm mạnh của nó — test đồng thời kiểm luôn
        // conf production), nên nó phải sinh passwd vào ĐÚNG đường mà conf đang trỏ tới. Sai chỗ
        // này thì broker chết ngay lúc khởi động với `Error: Unable to open pwfile`, và triệu
        // chứng ở tầng test chỉ là "container is not running" — không hề nhắc tới passwd.
        var bootstrap = string.Join(" && ",
            "mkdir -p /mosquitto/config/conf.d /mosquitto/config-src",
            $"mosquitto_passwd -c -b /mosquitto/config-src/passwd {BridgeUser} {Password}",
            $"mosquitto_passwd -b /mosquitto/config-src/passwd {DeviceA} {Password}",
            $"mosquitto_passwd -b /mosquitto/config-src/passwd {DeviceB} {Password}",
            // `mosquitto_passwd` chạy bằng root nhưng broker tụt quyền sang user `mosquitto`.
            // Thiếu chown là broker chết ngay với: Error: Unable to open pwfile.
            "chown mosquitto:mosquitto /mosquitto/config-src/passwd",
            "chmod 0700 /mosquitto/config-src/passwd",
            "exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf");

        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2.0")
            .WithResourceMapping(Path.Combine(cfgDir, "mosquitto.conf"), "/mosquitto/config/")
            .WithResourceMapping(Path.Combine(cfgDir, "acl.conf"), "/mosquitto/config/")
            .WithPortBinding(1883, true)
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand(bootstrap)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1883))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(MosquittoCollection))]
public sealed class MosquittoCollection : ICollectionFixture<MosquittoBrokerFixture>;
