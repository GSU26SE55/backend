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
/// <para>GeoLite2 không được commit vào repo vì license và vòng đời cập nhật. Production provision
/// file từ host rồi mount read-only; bộ test trong repo vẫn phải chốt cả chế độ optional của môi
/// trường local và chế độ required fail-fast của production.</para>
///
/// <para>Local/dev vẫn giữ <b>nhánh suy giảm êm</b> để enrichment optional không kéo sập audit.
/// Production bật <c>GeoIp:Required</c>, vì vậy thiếu hoặc hỏng DB phải dừng rollout ngay.</para>
/// </summary>
public class MaxMindGeoIpResolverTests
{
    private static MaxMindGeoIpResolver Build(
        string? dbPath,
        out MemoryCache cache,
        bool databaseRequired = false)
    {
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 });
        var settings = new Dictionary<string, string?>
        {
            ["GeoIp:Required"] = databaseRequired.ToString(),
        };
        if (dbPath is not null)
            settings["GeoIp:DbPath"] = dbPath;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
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
    public void Ctor_WithRequiredMissingDbFile_Throws()
    {
        var missingPath = $"khong-ton-tai/{Guid.NewGuid():N}/GeoLite2-City.mmdb";

        var act = () => Build(missingPath, out _, databaseRequired: true);

        act.Should().Throw<FileNotFoundException>()
            .Which.FileName.Should().Be(missingPath);
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

    [Fact]
    public void Ctor_WithRequiredCorruptDbFile_Throws()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"geoip-required-bogus-{Guid.NewGuid():N}.mmdb");
        File.WriteAllText(bogus, "đây không phải file MaxMind");

        try
        {
            var act = () => Build(bogus, out _, databaseRequired: true);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("GeoIp:Required is enabled*");
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
