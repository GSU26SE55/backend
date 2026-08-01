using SmsService.Application.DTOs.Response.Admin;
using SmsService.Infrastructure.BackgroundJobs;
using SmsService.Infrastructure.Options;

namespace SmsService.UnitTests.Contracts;

/// <summary>
/// Những mảnh nhỏ nhưng có hậu quả thật: DTO trả ra ngoài, giá trị mặc định của cấu hình, và bản
/// cài đặt rỗng của <c>ISmsGatewayNotifier</c>. Cả nhóm này trước đây phủ 0%.
/// </summary>
public class SmsContractSurfaceTests
{
    // ─────────────────────────────────────────────────────── GatewayDeviceDto

    /// <summary>
    /// <b>Điều đáng kiểm nhất ở DTO này không phải nó chứa gì, mà là nó KHÔNG chứa gì:</b>
    /// <c>ApiKeyHash</c> tuyệt đối không được lọt ra API quản trị. Đây là kiểm theo cấu trúc nên
    /// người sau thêm trường vào record cũng bị chặn.
    /// </summary>
    [Fact]
    public void GatewayDeviceDto_NeverExposesApiKeyHash()
    {
        var propertyNames = typeof(GatewayDeviceDto)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        propertyNames.Should().NotContain("ApiKeyHash");
        propertyNames.Should().NotContain(
            n => n.Contains("Key", StringComparison.OrdinalIgnoreCase)
              && n.Contains("Hash", StringComparison.OrdinalIgnoreCase),
            "khoá thiết bị chỉ hiện đúng một lần lúc cấp phát — lọt vào DTO danh sách là rò rỉ vĩnh viễn");
    }

    [Fact]
    public void GatewayDeviceDto_CarriesEveryFieldTheAdminScreenNeeds()
    {
        var id = Guid.NewGuid();
        var revokedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastSeen = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var createdAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var sentDate = new DateOnly(2026, 5, 2);

        var dto = new GatewayDeviceDto(
            id, "May gateway 1", "GW-001", true, revokedAt,
            100, 42, sentDate, lastSeen, "192.168.1.10", createdAt);

        dto.Id.Should().Be(id);
        dto.DeviceName.Should().Be("May gateway 1");
        dto.DeviceCode.Should().Be("GW-001");
        dto.IsActive.Should().BeTrue();
        dto.RevokedAt.Should().Be(revokedAt);
        dto.DailyLimit.Should().Be(100);
        dto.SentToday.Should().Be(42);
        dto.SentTodayDate.Should().Be(sentDate);
        dto.LastSeenAt.Should().Be(lastSeen);
        dto.LastSeenIp.Should().Be("192.168.1.10");
        dto.CreatedAt.Should().Be(createdAt);
    }

    /// <summary>
    /// <c>record</c> so sánh theo giá trị. Bộ test và mã gọi dựa vào điều đó; đổi sang <c>class</c>
    /// sẽ làm mọi phép so sánh im lặng chuyển thành so sánh tham chiếu.
    /// </summary>
    [Fact]
    public void GatewayDeviceDto_UsesValueEquality()
    {
        var id = Guid.NewGuid();
        var at = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        var a = new GatewayDeviceDto(id, "May 1", "GW-001", true, null, 100, 0, null, null, null, at);
        var b = new GatewayDeviceDto(id, "May 1", "GW-001", true, null, 100, 0, null, null, null, at);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ──────────────────────────────────────────────────────────── Options

    /// <summary>
    /// Giá trị mặc định là thứ chạy thật khi biến môi trường thiếu — tức là trạng thái phổ biến
    /// nhất. Chốt lại để không ai đổi nhầm mà không nhận ra.
    /// </summary>
    [Fact]
    public void OutboxOptions_HaveSaneDefaults()
    {
        var o = new OutboxOptions();

        o.BatchSize.Should().Be(50);
        o.PollIntervalSeconds.Should().Be(2);
        o.MaxRetries.Should().Be(10);
        OutboxOptions.SectionName.Should().Be("Outbox");
    }

    [Fact]
    public void SmsOptions_HaveSaneDefaults()
    {
        var o = new SmsOptions();

        o.DefaultDailyLimit.Should().Be(100);
        o.Provider.Should().Be("Gateway");
        o.From.Should().BeNull();
        SmsOptions.SectionName.Should().Be("Sms");
    }

    [Fact]
    public void OutboxOptions_AreWritable_SoConfigurationCanOverrideThem()
    {
        var o = new OutboxOptions { BatchSize = 10, PollIntervalSeconds = 1, MaxRetries = 3 };

        o.BatchSize.Should().Be(10);
        o.PollIntervalSeconds.Should().Be(1);
        o.MaxRetries.Should().Be(3);
    }
}
