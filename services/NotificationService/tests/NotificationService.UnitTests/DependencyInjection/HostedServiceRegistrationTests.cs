using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationService.Application.Consumers;
using NotificationService.Infrastructure.BackgroundJobs;

namespace NotificationService.UnitTests.DependencyInjection;

/// <summary>
/// Khoá danh sách worker nền được đăng ký.
///
/// <para><b>Vì sao có bộ test này:</b> ngày 2026-08-05, hai nhánh cùng dọn một dòng
/// <c>AddHostedService&lt;NotificationDispatchBackgroundService&gt;()</c> bị lặp — nhánh A bỏ dòng
/// trên, nhánh B bỏ dòng dưới. Git gộp lại thành bỏ CẢ HAI và **không báo xung đột**. Hệ quả:
/// consumer vẫn ghi đủ dòng notification, REST vẫn trả 200, nhưng không worker nào nhặt lên nên
/// mọi thông báo nằm im ở <c>Pending</c> — không ai nhận được gì. Build xanh, unit test xanh,
/// chỉ E2E mới lộ ra.</para>
///
/// <para>Kiểm bằng cách soi <see cref="IServiceCollection"/> chứ không dựng cả host, để không phải
/// có database/Redis/RabbitMQ thật.</para>
/// </summary>
public class HostedServiceRegistrationTests
{
    /// <summary>
    /// Các worker BẮT BUỘC phải được đăng ký. Thiếu bất kỳ cái nào là một tính năng chết lặng,
    /// không phải chỉ chậm đi.
    /// </summary>
    public static IEnumerable<object[]> RequiredWorkers() => new List<object[]>
    {
        new object[] { typeof(NotificationDispatchBackgroundService) },   // thiếu ⇒ KHÔNG AI nhận được thông báo
        new object[] { typeof(NotificationDigestBackgroundService) },
        new object[] { typeof(NotificationRetentionBackgroundService) },
        new object[] { typeof(NotificationDlqMonitorBackgroundService) },
        new object[] { typeof(ExpoReceiptReconcileBackgroundService) },   // ADR-0019 — tự nghỉ khi transport không có Expo
        new object[] { typeof(NotificationFallbackBackgroundService) },   // ADR-0019 — như trên
        new object[] { typeof(NotificationAuditOutboxRelayBackgroundService) },
    };

    [Theory]
    [MemberData(nameof(RequiredWorkers))]
    public void MoiWorkerBatBuoc_DuocDangKyDungMotLan(Type workerType)
    {
        var services = BuildServiceCollection();

        var matches = services
            .Where(d => d.ServiceType == typeof(IHostedService) && ImplementationTypeOf(d) == workerType)
            .ToList();

        matches.Should().HaveCount(1,
            $"{workerType.Name} phải được đăng ký đúng MỘT lần — 0 nghĩa là worker không bao giờ chạy, "
            + "nhiều hơn 1 nghĩa là người đọc code tưởng có nhiều worker song song.");
    }

    [Fact]
    public void KhongCoWorkerNaoBiDangKyTrungLap()
    {
        var services = BuildServiceCollection();

        var duplicates = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(ImplementationTypeOf)
            .Where(t => t is not null)
            .GroupBy(t => t!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        duplicates.Should().BeEmpty("đăng ký trùng làm người đọc tưởng hai worker cùng chạy");
    }

    [Fact]
    public void SlaAutoResumedConsumer_IsRegisteredByApplicationAssemblyScan()
    {
        var services = BuildServiceCollection();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(SlaAutoResumedConsumer),
            "AddMessageBus scans NotificationService.Application and ConfigureEndpoints creates this consumer's receive endpoint");
    }

    /// <summary>
    /// Dựng đúng phần đăng ký worker, không gọi <c>AddNotificationServiceInfrastructure</c> vì hàm
    /// đó còn cần chuỗi kết nối database và sẽ ném ngay nếu thiếu.
    /// </summary>
    private static IServiceCollection BuildServiceCollection()
    {
        var services = new ServiceCollection();

        // Cấu hình tối thiểu: các hàm Add* chỉ ĐĂNG KÝ service chứ không mở kết nối, nên
        // chuỗi kết nối giả là đủ.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationDb"] = "Host=localhost;Database=x;Username=x;Password=x",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["RabbitMQ:Host"] = "localhost",
                // AddSharedInfrastructure dựng handler JWT ngay lúc đăng ký (Encoding.GetBytes trên
                // khoá bí mật) nên thiếu ba khoá này là ném ArgumentNullException, chưa kịp tới phần
                // worker. Giá trị chỉ cần khác null — test này không xác thực token nào.
                ["JwtSettings:SecretKey"] = "khoa-gia-cho-unit-test-du-dai-de-hmac-sha256-chap-nhan",
                ["JwtSettings:Issuer"] = "test-issuer",
                ["JwtSettings:Audience"] = "test-audience",
            })
            .Build();

        NotificationService.Infrastructure.DependencyInjection.ManageDependencyInjection
            .AddNotificationServiceInfrastructure(services, configuration);

        return services;
    }

    private static Type? ImplementationTypeOf(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType
        ?? descriptor.ImplementationInstance?.GetType();
}
