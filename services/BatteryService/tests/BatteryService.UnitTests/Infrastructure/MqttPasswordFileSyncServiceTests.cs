using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.Infrastructure.Mqtt;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// GH-784 — đưa thông tin đăng nhập thiết bị xuống file <c>passwd</c> của broker.
///
/// <para>
/// Không có bước này thì API sinh username/password, lưu DB, trả cho thiết bị — nhưng Mosquitto
/// không hề biết, và thiết bị nhận "Connection Refused: not authorised" trong khi mọi tầng phía
/// trên đều báo thành công.
/// </para>
/// </summary>
public class MqttPasswordFileSyncServiceTests : IDisposable
{
    private const string BridgeLine =
        "backend-bridge:$7$101$uCMww8d0vIaVeM8v$uBKXw6H96ImJogsA/38iz1zvY7cwiHE4j8416rXB5ASOtAEcFyX3Tlv9hbCjut66eInAOk/3WrCs7VrvkMFTQA==";

    private readonly string _dir = Directory.CreateTempSubdirectory("gh784-sync-").FullName;

    private string PasswdPath => Path.Combine(_dir, "passwd");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static IotDevice Device(
        string code, IotDeviceStatusEnum status = IotDeviceStatusEnum.Active,
        string? hash = "$7$10000$c2FsdA==$aGFzaA==", bool deleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            DeviceCode = code.ToUpperInvariant(),
            DisplayName = code,
            SiteId = Guid.NewGuid(),
            Status = status,
            ApiKeyHash = "h",
            ApiKeyLastFour = "abcd",
            ApiKeyScopes = IotApiKeyScopeEnum.EdgeDeviceDefault,
            HeartbeatIntervalSeconds = 60,
            MqttUsername = code.ToLowerInvariant(),
            MqttPasswordHash = hash,
            IsDeleted = deleted,
        };

    private MqttPasswordFileSyncService Service(params IotDevice[] devices)
    {
        var uow = new MockUnitOfWorkBuilder().WithIotDevices(devices);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IBatteryUnitOfWork))).Returns(uow.Build());
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);

        return new MqttPasswordFileSyncService(
            factory.Object,
            Options.Create(new MqttOptions { Enabled = true, PasswordFilePath = PasswdPath }),
            NullLogger<MqttPasswordFileSyncService>.Instance);
    }

    [Fact]
    public async Task Sync_WritesActiveDevices_AndKeepsTheBridgeAccount()
    {
        await File.WriteAllTextAsync(PasswdPath, BridgeLine + "\n");

        await Service(Device("gw-001"), Device("gw-002")).SyncOnceAsync(CancellationToken.None);

        var content = await File.ReadAllTextAsync(PasswdPath);
        content.Should().Contain("gw-001:").And.Contain("gw-002:");
        content.Should().Contain(BridgeLine,
            "xoá backend-bridge là chính cầu nối backend↔broker tự khoá mình ra ngoài");
    }

    [Theory]
    [InlineData(IotDeviceStatusEnum.Disabled)]
    [InlineData(IotDeviceStatusEnum.Decommissioned)]
    public async Task Sync_ExcludesDisabledDevices(IotDeviceStatusEnum status)
    {
        // Vô hiệu hoá mà vẫn để trong file thì thu hồi chỉ có tác dụng trên giấy tờ.
        await File.WriteAllTextAsync(PasswdPath, BridgeLine + "\n");

        await Service(Device("gw-ok"), Device("gw-bad", status)).SyncOnceAsync(CancellationToken.None);

        var content = await File.ReadAllTextAsync(PasswdPath);
        content.Should().Contain("gw-ok:");
        content.Should().NotContain("gw-bad");
    }

    [Fact]
    public async Task Sync_KeepsOfflineDevices()
    {
        // Mất kết nối là chuyện tạm thời — rút quyền của nó thì thiết bị không bao giờ nối lại được.
        await Service(Device("gw-off", IotDeviceStatusEnum.Offline)).SyncOnceAsync(CancellationToken.None);

        (await File.ReadAllTextAsync(PasswdPath)).Should().Contain("gw-off:");
    }

    [Fact]
    public async Task Sync_ExcludesSoftDeletedDevices()
    {
        await Service(Device("gw-live"), Device("gw-gone", deleted: true))
            .SyncOnceAsync(CancellationToken.None);

        var content = await File.ReadAllTextAsync(PasswdPath);
        content.Should().Contain("gw-live:").And.NotContain("gw-gone");
    }

    [Fact]
    public async Task Sync_SkipsDevicesWithoutHash()
    {
        // Một bản ghi hỏng làm Mosquitto từ chối nạp CẢ file ⇒ mất quyền của tất cả.
        await Service(Device("gw-ok"), Device("gw-nohash", hash: null))
            .SyncOnceAsync(CancellationToken.None);

        var content = await File.ReadAllTextAsync(PasswdPath);
        content.Should().Contain("gw-ok:").And.NotContain("gw-nohash");
    }

    [Fact]
    public async Task Sync_DoesNotRewriteTheFile_WhenNothingChanged()
    {
        // Mỗi lần ghi kéo theo một lần broker nạp lại. Ghi lại nội dung y hệt là quấy broker vô cớ.
        var service = Service(Device("gw-001"));
        await service.SyncOnceAsync(CancellationToken.None);
        var firstWrite = File.GetLastWriteTimeUtc(PasswdPath);

        await Task.Delay(50);
        await service.SyncOnceAsync(CancellationToken.None);

        File.GetLastWriteTimeUtc(PasswdPath).Should().Be(firstWrite);
    }

    [Fact]
    public async Task Sync_RewritesWhenADeviceIsRevoked()
    {
        await Service(Device("gw-001"), Device("gw-002")).SyncOnceAsync(CancellationToken.None);
        (await File.ReadAllTextAsync(PasswdPath)).Should().Contain("gw-002:");

        // Lượt sau chỉ còn một thiết bị ⇒ thiết bị kia phải BIẾN MẤT khỏi file.
        await Service(Device("gw-001")).SyncOnceAsync(CancellationToken.None);

        var content = await File.ReadAllTextAsync(PasswdPath);
        content.Should().Contain("gw-001:").And.NotContain("gw-002");
    }

    [Fact]
    public async Task Sync_CreatesTheFile_WhenItDoesNotExistYet()
    {
        File.Exists(PasswdPath).Should().BeFalse();

        await Service(Device("gw-001")).SyncOnceAsync(CancellationToken.None);

        File.Exists(PasswdPath).Should().BeTrue();
        (await File.ReadAllTextAsync(PasswdPath)).Should().Contain("gw-001:");
    }

    [Fact]
    public async Task Sync_LeavesNoTempFileBehind()
    {
        // Ghi qua file tạm rồi đổi tên; sót .tmp nghĩa là lần ghi trước hỏng giữa chừng.
        await Service(Device("gw-001")).SyncOnceAsync(CancellationToken.None);

        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_WritesFileOnlyReadableByOwner()
    {
        // Mosquitto 2.0 TỪ CHỐI nạp file mà người khác đọc được — quyền sai là broker không lên.
        if (OperatingSystem.IsWindows()) return;

        await Service(Device("gw-001")).SyncOnceAsync(CancellationToken.None);

        var mode = File.GetUnixFileMode(PasswdPath);
        mode.Should().NotHaveFlag(UnixFileMode.OtherRead);
        mode.Should().NotHaveFlag(UnixFileMode.GroupRead);
    }
}
