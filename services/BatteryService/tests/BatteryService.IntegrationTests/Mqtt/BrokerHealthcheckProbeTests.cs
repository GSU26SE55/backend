using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Xunit;

namespace BatteryService.IntegrationTests.Mqtt;

/// <summary>
/// GH-786 — healthcheck của Mosquitto trong root compose luôn báo <c>unhealthy</c>.
///
/// <para>
/// Nguyên nhân: probe dùng <c>sh -c '&lt;/dev/tcp/127.0.0.1/1883'</c>, mà <c>/dev/tcp</c> là tính
/// năng của <b>bash</b> — image <c>eclipse-mosquitto:2.0</c> dùng BusyBox ash. Kiểm chứng trực tiếp
/// trong container: <c>sh: can't open /dev/tcp/127.0.0.1/1883: no such file</c>. Broker phục vụ
/// bình thường (bridge nối được, publish QoS 1 thành công) nhưng container vĩnh viễn unhealthy —
/// và mọi thứ `depends_on: service_healthy` sẽ không bao giờ khởi động.
/// </para>
/// <para>
/// Test này chạy CHÍNH lệnh probe trong container thật, cả chiều đúng lẫn chiều sai. Kiểm bằng
/// cách đọc chuỗi trong <c>docker-compose.yml</c> thì chỉ chứng minh "chuỗi đã đổi", không chứng
/// minh probe chạy được.
/// </para>
/// </summary>
public sealed class BrokerHealthcheckProbeTests : IAsyncLifetime
{
    private const string HealthUser = "backend-bridge";
    private const string HealthPassword = "health-probe-pw";

    private IContainer _container = null!;

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Không tìm thấy gốc repo từ " + AppContext.BaseDirectory);
            return dir!.FullName;
        }
    }

    private static string MqttConfigDir => Path.Combine(RepoRoot, "infra", "mqtt", "mosquitto", "config");

    public async Task InitializeAsync()
    {
        var bootstrap = string.Join(" && ",
            // IOT3-106/M1 — `password_file` của repo nay là `/mosquitto/config-src/passwd`
            // (đường của một THƯ MỤC được mount) thay vì `/mosquitto/config/passwd` (file lẻ).
            // Test nạp CHÍNH conf của repo nên phải sinh passwd vào đúng đường đó.
            "mkdir -p /mosquitto/config/conf.d /mosquitto/config-src",
            $"mosquitto_passwd -c -b /mosquitto/config-src/passwd {HealthUser} {HealthPassword}",
            "chown mosquitto:mosquitto /mosquitto/config-src/passwd",
            "chmod 0700 /mosquitto/config-src/passwd",
            "exec /usr/sbin/mosquitto -c /mosquitto/config/mosquitto.conf");

        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2.0")
            .WithResourceMapping(Path.Combine(MqttConfigDir, "mosquitto.conf"), "/mosquitto/config/")
            .WithResourceMapping(Path.Combine(MqttConfigDir, "acl.conf"), "/mosquitto/config/")
            .WithPortBinding(1883, true)
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand(bootstrap)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1883))
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private Task<DotNet.Testcontainers.Containers.ExecResult> ExecAsync(string command)
        => _container.ExecAsync(["/bin/sh", "-c", command]);

    [Fact]
    public async Task OldProbe_UsingDevTcp_FailsInThisImage()
    {
        // Ghim NGUYÊN NHÂN, không chỉ ghim bản sửa: nếu ai đó quay lại /dev/tcp vì thấy nó "gọn hơn",
        // test này nói rõ vì sao không được.
        var result = await ExecAsync("sh -c '</dev/tcp/127.0.0.1/1883'");

        result.ExitCode.Should().NotBe(0, "BusyBox ash không có /dev/tcp — đó là tính năng của bash");
        (result.Stderr + result.Stdout).Should().Contain("/dev/tcp");
    }

    [Fact]
    public async Task NewProbe_AuthenticatedPublish_SucceedsWhenBrokerIsUsable()
    {
        var result = await ExecAsync(
            $"mosquitto_pub -h 127.0.0.1 -p 1883 -u {HealthUser} -P {HealthPassword} " +
            "-t solar/healthcheck -m ok -q 1");

        result.ExitCode.Should().Be(0, result.Stderr);
    }

    [Fact]
    public async Task NewProbe_FailsOnWrongPassword_SoAuthPathIsActuallyChecked()
    {
        // Chiều âm của tiêu chí nghiệm thu: mở được socket mà auth hỏng thì PHẢI báo unhealthy.
        // Không có khẳng định này thì probe mới có thể chỉ là "kết nối được" trá hình.
        var result = await ExecAsync(
            $"mosquitto_pub -h 127.0.0.1 -p 1883 -u {HealthUser} -P sai-mat-khau " +
            "-t solar/healthcheck -m ok -q 1");

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task NewProbe_FailsWhenNoCredentialsGiven()
    {
        // Cấu hình thiếu Mqtt__Password ⇒ probe chạy với mật khẩu rỗng. Phải unhealthy, vì lúc đó
        // broker thật sự KHÔNG dùng được cho backend.
        var result = await ExecAsync(
            "mosquitto_pub -h 127.0.0.1 -p 1883 -t solar/healthcheck -m ok -q 1");

        result.ExitCode.Should().NotBe(0, "broker cấm anonymous — probe không được coi đó là khoẻ");
    }

    [Fact]
    public void ComposeHealthcheck_UsesTheProbeThisTestProved_NotDevTcp()
    {
        // Nối bản sửa với bằng chứng: các test trên chứng minh lệnh nào chạy được; test này bảo đảm
        // docker-compose.yml dùng ĐÚNG lệnh đó, chứ không phải một biến thể chưa ai thử.
        var compose = File.ReadAllText(Path.Combine(RepoRoot, "docker-compose.yml"));
        var mosquittoBlock = Regex.Match(
            compose, @"^  mosquitto:\r?\n(?:.*\r?\n)*?(?=^  \S)", RegexOptions.Multiline).Value;
        mosquittoBlock.Should().NotBeEmpty("không tìm thấy khối service mosquitto");

        // Chỉ soi DÒNG LỆNH probe, không soi cả khối: phần chú thích có nhắc "/dev/tcp" để giải
        // thích vì sao không dùng nó — soi cả khối sẽ bắt nhầm chính lời giải thích đó.
        var probeLine = mosquittoBlock
            .Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("test:", StringComparison.Ordinal));

        probeLine.Should().NotBeNull("khối mosquitto phải có healthcheck");
        probeLine!.Should().NotContain("/dev/tcp",
            "probe /dev/tcp không chạy được trong image này — xem OldProbe_UsingDevTcp_FailsInThisImage");
        probeLine.Should().Contain("mosquitto_pub");
        probeLine.Should().Contain("-q 1", "QoS 1 buộc broker ACK ⇒ phủ cả đường xác thực");
    }
}
