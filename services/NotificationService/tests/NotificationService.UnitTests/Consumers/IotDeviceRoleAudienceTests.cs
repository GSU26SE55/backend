using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// 30/08/2026 — Staff bị bỏ khỏi nhóm nhận thông báo vòng đời thiết bị IoT: hai consumer offline và
/// recovered nay chỉ hỏi Manager + Admin.
///
/// Cần một file test riêng vì các test IoT sẵn có chỉ đếm số dòng notification ghi ra, mà số đó
/// không đổi khi thêm/bớt role — mock resolver trả về cùng danh sách recipient cho mọi string[].
/// Nói cách khác, việc bỏ "Staff" không làm test nào đỏ. Những test dưới đây khẳng định thẳng
/// bộ role mà consumer hỏi, nên nếu ai đó thêm Staff trở lại thì sẽ thấy ngay.
/// </summary>
public sealed class IotDeviceRoleAudienceTests
{
    private static IotDeviceWentOfflineEvent MakeOfflineEvent() => new(
        IotDeviceId: Guid.NewGuid(),
        DeviceCode: "DEV-01",
        DisplayName: "Gateway A",
        SiteId: Guid.NewGuid(),
        SiteName: "Site Hanoi",
        LastSeenAt: new DateTime(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc),
        DetectedAt: new DateTime(2026, 6, 22, 10, 6, 0, DateTimeKind.Utc),
        OfflineDurationSeconds: 360,
        AffectedBatteryCount: 3,
        AlertId: Guid.NewGuid());

    private static IotDeviceRecoveredEvent MakeRecoveredEvent() => new(
        IotDeviceId: Guid.NewGuid(),
        DeviceCode: "GW-01",
        DisplayName: "Gateway 01",
        SiteId: Guid.NewGuid(),
        SiteName: "Site A",
        RecoveredAt: new DateTime(2026, 6, 22, 10, 30, 0, DateTimeKind.Utc),
        LastOfflineAt: new DateTime(2026, 6, 22, 10, 6, 0, DateTimeKind.Utc),
        AlertId: Guid.NewGuid());

    [Fact]
    public async Task WentOffline_AsksForManagerAndAdminOnly_NotStaff()
    {
        var roles = new List<string[]>();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>(rolesRequested: roles);

        await harness.Bus.Publish(MakeOfflineEvent());
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        roles.Should().ContainSingle();
        roles[0].Should().BeEquivalentTo(new[] { "Manager", "Admin" });
        roles[0].Should().NotContain("Staff");

        // Bỏ role không được làm hỏng đường ghi: vẫn phải ra notification cho những role còn lại.
        written.Should().NotBeEmpty();
        await harness.Stop();
    }

    [Fact]
    public async Task Recovered_AsksForManagerAndAdminOnly_NotStaff()
    {
        var roles = new List<string[]>();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceRecoveredConsumer>(rolesRequested: roles);

        await harness.Bus.Publish(MakeRecoveredEvent());
        (await harness.Consumed.Any<IotDeviceRecoveredEvent>()).Should().BeTrue();

        roles.Should().ContainSingle();
        roles[0].Should().BeEquivalentTo(new[] { "Manager", "Admin" });
        roles[0].Should().NotContain("Staff");

        written.Should().NotBeEmpty();
        await harness.Stop();
    }

    /// <summary>
    /// Customer của site vẫn nhận như cũ — thay đổi chỉ động tới nhóm nội bộ, không được vô tình
    /// cắt luôn người dùng cuối.
    /// </summary>
    [Fact]
    public async Task WentOffline_StillNotifiesSiteCustomer()
    {
        var operationsUser = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var (harness, written, _) =
            await ConsumerTestHarness.StartAsync<IotDeviceWentOfflineConsumer>(
                new[] { operationsUser });

        await harness.Bus.Publish(MakeOfflineEvent() with { CustomerId = customer });
        (await harness.Consumed.Any<IotDeviceWentOfflineEvent>()).Should().BeTrue();

        written.Select(n => n.UserId).Distinct()
            .Should().BeEquivalentTo(new[] { operationsUser, customer });
        await harness.Stop();
    }
}
