using BatteryService.Application.Interfaces;
using BatteryService.Application.Mqtt;
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
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
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

    /// <summary>
    /// IOT3-88 — vòng đời đầy đủ qua file thật: cấp thiết bị → thu hồi → dòng cầu nối còn nguyên.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hai bài trước đã kiểm riêng lẻ "thiết bị Disabled biến mất" và "dòng bridge còn". Bài này
    /// kiểm cái mà cả hai bỏ sót: <b>vị trí</b>. Thu hồi phải xoá dòng khỏi <i>vùng có mốc</i>,
    /// còn <c>backend-bridge</c> phải nằm <i>ngoài</i> mốc và không suy suyển — kể cả sau nhiều
    /// lượt đồng bộ liên tiếp.
    /// </para>
    /// <para>
    /// Vì sao vị trí quan trọng: <c>MosquittoPasswordFile.Compose()</c> dựng lại TOÀN BỘ phần
    /// trong mốc mỗi lần chạy. Nếu dòng bridge lọt vào trong, lần cấp thiết bị kế tiếp sẽ xoá mất
    /// nó, BatteryService tự khoá mình ra khỏi broker, và <b>toàn bộ</b> telemetry MQTT chết —
    /// không phải một thiết bị.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Sync_RemovesRevokedDeviceFromManagedRegion_ButNeverTouchesTheBridgeLine()
    {
        const string operatorNote = "# tài khoản vận hành thêm tay — KHÔNG được mất";
        await File.WriteAllTextAsync(PasswdPath, BridgeLine + "\n" + operatorNote + "\n");

        // --- lượt 1: cấp hai thiết bị ---
        await Service(Device("gw-a"), Device("gw-b")).SyncOnceAsync(CancellationToken.None);

        var afterGrant = await File.ReadAllTextAsync(PasswdPath);
        InsideManagedRegion(afterGrant).Should().Contain("gw-a:").And.Contain("gw-b:");
        OutsideManagedRegion(afterGrant).Should().Contain(BridgeLine).And.Contain(operatorNote);

        // --- lượt 2: thu hồi gw-b ---
        await Service(Device("gw-a"), Device("gw-b", IotDeviceStatusEnum.Disabled))
            .SyncOnceAsync(CancellationToken.None);

        var afterRevoke = await File.ReadAllTextAsync(PasswdPath);

        // Biến mất khỏi VÙNG CÓ MỐC — không chỉ "không xuất hiện ở đâu đó trong file".
        InsideManagedRegion(afterRevoke).Should().Contain("gw-a:").And.NotContain("gw-b");

        // Dòng cầu nối và ghi chú của người vận hành còn NGUYÊN VĂN.
        OutsideManagedRegion(afterRevoke).Should().Contain(BridgeLine).And.Contain(operatorNote);

        // Và chỉ có ĐÚNG MỘT dòng bridge — mỗi lượt đồng bộ nhân bản nó lên thì Mosquitto sẽ
        // đọc dòng cuối, tức mật khẩu có thể là bản cũ mà không ai nhận ra.
        afterRevoke.Split('\n').Count(l => l.StartsWith("backend-bridge:")).Should().Be(1);
    }

    /// <summary>Phần nằm GIỮA hai mốc — do BatteryService dựng lại mỗi lượt.</summary>
    private static string InsideManagedRegion(string content)
    {
        var begin = content.IndexOf(MosquittoPasswordFile.BeginMarker, StringComparison.Ordinal);
        var end = content.IndexOf(MosquittoPasswordFile.EndMarker, StringComparison.Ordinal);
        begin.Should().BeGreaterThanOrEqualTo(0, "phải có mốc mở");
        end.Should().BeGreaterThan(begin, "phải có mốc đóng sau mốc mở");
        return content[(begin + MosquittoPasswordFile.BeginMarker.Length)..end];
    }

    /// <summary>Phần NGOÀI hai mốc — giữ nguyên từng ký tự.</summary>
    private static string OutsideManagedRegion(string content)
    {
        var begin = content.IndexOf(MosquittoPasswordFile.BeginMarker, StringComparison.Ordinal);
        var end = content.IndexOf(MosquittoPasswordFile.EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
            return content;
        return content[..begin] + content[(end + MosquittoPasswordFile.EndMarker.Length)..];
    }

    [Fact]
    public async Task Sync_WritesFileReadableByBrokerButNotWritable()
    {
        // IOT3-22 — bài này trước đây khẳng định 0600 ("chỉ chủ đọc được").
        //
        // Khẳng định đó khoá chặt một cấu hình KHÔNG BAO GIỜ chạy được: container backend
        // chạy root, còn eclipse-mosquitto chạy uid 1883 ⇒ file 0600 của root thì broker
        // không đọc nổi, và mọi thiết bị bị từ chối với state=4 BAD_CREDENTIALS.
        //
        // Nay khẳng định đúng bất biến cần giữ: BROKER ĐỌC ĐƯỢC, nhưng KHÔNG AI NGOÀI CHỦ
        // GHI ĐƯỢC. File chỉ chứa hash PBKDF2-SHA512 `$7$`, không có plaintext.
        if (OperatingSystem.IsWindows())
            return;

        await Service(Device("gw-001")).SyncOnceAsync(CancellationToken.None);

        var mode = File.GetUnixFileMode(PasswdPath);

        // Đọc được từ uid khác — điều kiện cần để Mosquitto nạp file.
        mode.Should().HaveFlag(UnixFileMode.OtherRead);
        mode.Should().HaveFlag(UnixFileMode.UserRead);
        mode.Should().HaveFlag(UnixFileMode.UserWrite);

        // Nhưng KHÔNG ai ngoài chủ được ghi — nếu không thì broker (hoặc bất kỳ tiến trình
        // nào trong cùng máy) có thể tự cấp quyền cho một thiết bị bịa ra.
        mode.Should().NotHaveFlag(UnixFileMode.GroupWrite);
        mode.Should().NotHaveFlag(UnixFileMode.OtherWrite);

        // Và không bao giờ được là file thực thi.
        mode.Should().NotHaveFlag(UnixFileMode.UserExecute);
        mode.Should().NotHaveFlag(UnixFileMode.GroupExecute);
        mode.Should().NotHaveFlag(UnixFileMode.OtherExecute);
    }
}
