using AuditAggregatorService.Infrastructure.Implements;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuditAggregatorService.IntegrationTests;

/// <summary>
/// <b><c>#AUDIT-16</c> — geo enrichment.</b>
///
/// <para><b>Sự thật cần biết trước khi đọc:</b> file <c>GeoLite2-City.mmdb</c> <b>KHÔNG có trong
/// repo</b> (đã tìm toàn bộ cây thư mục ngày 2026-08-01). Nghĩa là ở mọi môi trường hiện tại —
/// gồm cả production — <see cref="MaxMindGeoIpResolver"/> chạy ở chế độ <i>tắt</i>: constructor
/// log cảnh báo rồi mọi <c>Lookup</c> trả <c>null</c>. Quyết định "Geo IP = MaxMind" hiện là quyết
/// định trên giấy.</para>
///
/// <para>Vì vậy bộ test này chốt đúng thứ đang chạy thật: <b>nhánh suy giảm êm</b>. Điều tối kỵ là
/// resolver ném exception khi thiếu file — enrichment chỉ là phần thêm nếm, nó mà ném thì kéo sập
/// cả pipeline audit. Khi nào file <c>.mmdb</c> được đưa vào repo thì bổ sung test tra cứu thật.</para>
/// </summary>
public class MaxMindGeoIpResolverTests
{
    private static MaxMindGeoIpResolver Build(string? dbPath, out MemoryCache cache)
    {
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dbPath is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["GeoIp:DbPath"] = dbPath })
            .Build();

        return new MaxMindGeoIpResolver(config, cache, NullLogger<MaxMindGeoIpResolver>.Instance);
    }

    [Fact]
    public void Ctor_WithMissingDbFile_DoesNotThrow()
    {
        var act = () => Build("khong-ton-tai/GeoLite2-City.mmdb", out _);

        act.Should().NotThrow(
            "thiếu file .mmdb là trạng thái THẬT hiện nay; ném ở đây sẽ chặn cả pipeline audit");
    }

    [Fact]
    public void Ctor_WithNoConfiguredPath_FallsBackToDefault_AndDoesNotThrow()
    {
        // Không khai GeoIp:DbPath → dùng mặc định "geoip/GeoLite2-City.mmdb", cũng không tồn tại.
        var act = () => Build(null, out _);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Trỏ vào một file CÓ TỒN TẠI nhưng không phải định dạng mmdb: <c>DatabaseReader</c> ném lúc
    /// khởi tạo. Constructor phải nuốt và chuyển sang chế độ tắt — nhánh <c>catch</c> này khác hẳn
    /// nhánh "file không tồn tại".
    /// </summary>
    [Fact]
    public void Ctor_WithCorruptDbFile_SwallowsAndDisables()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"geoip-bogus-{Guid.NewGuid():N}.mmdb");
        File.WriteAllText(bogus, "đây không phải file MaxMind");

        try
        {
            MaxMindGeoIpResolver? resolver = null;
            var act = () => resolver = Build(bogus, out _);

            act.Should().NotThrow();
            resolver!.Lookup("8.8.8.8").Should().BeNull("DB hỏng ⇒ coi như không có DB");
            resolver.Dispose();
        }
        finally
        {
            File.Delete(bogus);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-ip")]
    [InlineData("999.999.999.999")]
    public void Lookup_InvalidInput_ReturnsNull(string? ip)
    {
        var resolver = Build("khong-ton-tai/GeoLite2-City.mmdb", out _);

        resolver.Lookup(ip).Should().BeNull();

        resolver.Dispose();
    }

    [Fact]
    public void Lookup_ValidIp_WithoutDb_ReturnsNull()
    {
        var resolver = Build("khong-ton-tai/GeoLite2-City.mmdb", out _);

        resolver.Lookup("8.8.8.8").Should().BeNull();
        resolver.Lookup("2001:4860:4860::8888").Should().BeNull("IPv6 cũng phải suy giảm êm");

        resolver.Dispose();
    }

    [Fact]
    public void Dispose_WithoutDb_DoesNotThrow()
    {
        var resolver = Build("khong-ton-tai/GeoLite2-City.mmdb", out _);

        var act = () =>
        {
            resolver.Dispose();
            resolver.Dispose(); // gọi hai lần phải an toàn
        };

        act.Should().NotThrow();
    }
}
