using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApiGateway.UnitTests;

/// <summary>
/// GH-787 — ở Production, mọi cluster của gateway trỏ về <c>localhost</c>.
///
/// <para>
/// <c>docker-compose.prod.yml</c> đặt <c>ASPNETCORE_ENVIRONMENT=Production</c>, nhưng gateway
/// KHÔNG có <c>appsettings.Production.json</c> và compose cũng không override
/// <c>ReverseProxy__</c>. Chỉ <c>appsettings.json</c> được nạp — mà file đó trỏ
/// <c>https://localhost:7000/7100/7200/…</c>. Trong container, <c>localhost</c> chính là
/// ApiGateway, không phải microservice ⇒ mọi route proxy trả 502.
/// </para>
/// <para>
/// Test nạp cấu hình ĐÚNG THỨ TỰ mà ASP.NET nạp (base → theo môi trường → biến môi trường), thay vì
/// so chuỗi trong file JSON: so chuỗi chỉ chứng minh "file có tồn tại", không chứng minh giá trị nào
/// thắng sau khi hợp nhất.
/// </para>
/// </summary>
public class ReverseProxyDestinationTests
{
    /// <summary>Bảy cluster khai trong <c>appsettings.json</c> — thiếu một cái là thủng một tuyến.</summary>
    public static TheoryData<string> AllClusters()
    {
        var data = new TheoryData<string>();
        foreach (var cluster in new[]
                 {
                     "authCluster", "fileStorageCluster", "batteryCluster", "ticketCluster",
                     "notificationCluster", "smsCluster", "auditAggregatorCluster",
                 })
        {
            data.Add(cluster);
        }
        return data;
    }

    private static string GatewayDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SolarBatteryMaintainance.slnx")))
                dir = dir.Parent;
            Assert.True(dir is not null, "Không tìm thấy gốc repo từ " + AppContext.BaseDirectory);
            return Path.Combine(dir!.FullName, "services", "ApiGateway", "src", "ApiGateway");
        }
    }

    /// <summary>Dựng cấu hình y như <c>WebApplication.CreateBuilder</c> làm cho môi trường đã cho.</summary>
    private static IConfigurationRoot BuildConfig(string environment, IDictionary<string, string?>? env = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(GatewayDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);

        if (env is not null)
            builder.AddInMemoryCollection(env);   // đứng vị trí của biến môi trường: nạp SAU cùng

        return builder.Build();
    }

    private static string? Destination(IConfigurationRoot config, string cluster)
        => config[$"ReverseProxy:Clusters:{cluster}:Destinations:destination1:Address"];

    [Theory]
    [MemberData(nameof(AllClusters))]
    public void Production_ResolvesEveryClusterToContainerDns_NotLocalhost(string cluster)
    {
        var address = Destination(BuildConfig("Production"), cluster);

        address.Should().NotBeNullOrEmpty($"cluster {cluster} phải có destination");
        address.Should().NotContain("localhost",
            "trong container, localhost CHÍNH LÀ ApiGateway — trỏ về đó là 502 cho mọi request");
        address.Should().StartWith("http://", "giao tiếp nội bộ container dùng HTTP cổng 8080");
    }

    [Theory]
    [MemberData(nameof(AllClusters))]
    public void Docker_AndProduction_PointToTheSamePlace(string cluster)
    {
        // Hai môi trường cùng chạy trong container nên phải cùng đích. Lệch nhau nghĩa là một trong
        // hai file bị sửa mà quên file kia — đúng loại lỗi sinh ra chính issue này.
        Destination(BuildConfig("Production"), cluster)
            .Should().Be(Destination(BuildConfig("Docker"), cluster));
    }

    [Fact]
    public void BaseAppsettings_StillUsesLocalhost_ForLocalDevelopment()
    {
        // Chống sửa quá tay: chạy `dotnet run` ngoài container vẫn phải trỏ localhost. Sửa file gốc
        // sẽ làm hỏng đường phát triển tại chỗ mà không ai để ý ngay.
        var address = Destination(BuildConfig("KhongTonTai"), "authCluster");

        address.Should().Contain("localhost");
    }

    [Fact]
    public void EnvironmentVariable_OverridesTheFile()
    {
        // Đường thoát cho vận hành: trỏ sang host riêng / service mesh mà không phải build lại image.
        var config = BuildConfig("Production", new Dictionary<string, string?>
        {
            ["ReverseProxy:Clusters:authCluster:Destinations:destination1:Address"] = "http://auth.internal:9000",
        });

        Destination(config, "authCluster").Should().Be("http://auth.internal:9000");
    }

    [Fact]
    public void Production_KeepsTicketClusterHttpTuning()
    {
        // ticketCluster có cấu hình riêng (HTTP/1.1 + timeout 90s) cho luồng chat/voice dài. Chép
        // thiếu phần này thì request dài bị cắt giữa chừng — và triệu chứng sẽ trông như lỗi mạng.
        var config = BuildConfig("Production");

        config["ReverseProxy:Clusters:ticketCluster:HttpRequest:ActivityTimeout"].Should().Be("00:01:30");
        config["ReverseProxy:Clusters:ticketCluster:HttpClient:RequestVersion"].Should().Be("1.1");
    }

    [Fact]
    public void ProductionFile_Exists_AndIsShippedWithTheApp()
    {
        // Không có file này thì Production rơi về appsettings.json (localhost) — chính lỗi GH-787.
        File.Exists(Path.Combine(GatewayDir, "appsettings.Production.json")).Should().BeTrue();
    }
}
