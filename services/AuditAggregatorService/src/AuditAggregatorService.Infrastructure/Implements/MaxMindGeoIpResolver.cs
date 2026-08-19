using System.Net;
using AuditAggregatorService.Application.Interfaces;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuditAggregatorService.Infrastructure.Implements;

/// <summary>
/// Geo IP resolver dùng MaxMind GeoLite2 (Sprint audit <c>#AUDIT-16</c>, D11).
///
/// <para>Load file <c>.mmdb</c> từ path config <c>GeoIp:DbPath</c> (mặc định <c>geoip/GeoLite2-City.mmdb</c>).
/// Khi <c>GeoIp:Required=false</c>, file thiếu/hỏng vẫn graceful fallback về <c>null</c>. Production đặt
/// <c>GeoIp:Required=true</c> để fail-fast khi database vận hành chưa được provision đúng. Lỗi lookup
/// của một địa chỉ riêng lẻ luôn fallback về <c>null</c>. Cache LRU 10k entry TTL 1h để đạt hit ≥ 80%.</para>
///
/// Singleton — <see cref="DatabaseReader"/> thread-safe cho concurrent read.
/// </summary>
public sealed class MaxMindGeoIpResolver : IGeoIpResolver, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxMindGeoIpResolver> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public MaxMindGeoIpResolver(IConfiguration configuration, IMemoryCache cache, ILogger<MaxMindGeoIpResolver> logger)
    {
        _cache = cache;
        _logger = logger;

        var dbPath = configuration["GeoIp:DbPath"] ?? "geoip/GeoLite2-City.mmdb";
        var databaseRequired = configuration.GetValue("GeoIp:Required", false);

        if (!File.Exists(dbPath))
        {
            if (databaseRequired)
            {
                throw new FileNotFoundException(
                    $"GeoIp:Required is enabled but the MaxMind database does not exist at '{dbPath}'.",
                    dbPath);
            }

            _logger.LogWarning("[GeoIp] Không tìm thấy {Path} — geo enrichment disabled (fallback null).", dbPath);
            return;
        }

        try
        {
            _reader = new DatabaseReader(dbPath);
            _logger.LogInformation("[GeoIp] MaxMind GeoLite2 loaded từ {Path}.", dbPath);
        }
        catch (Exception ex) when (databaseRequired)
        {
            throw new InvalidOperationException(
                $"GeoIp:Required is enabled but the MaxMind database at '{dbPath}' could not be loaded.",
                ex);
        }
        catch (Exception ex) when (!databaseRequired)
        {
            _logger.LogWarning(ex, "[GeoIp] Lỗi load MaxMind DB — geo enrichment disabled.");
        }
    }

    public GeoIpResult? Lookup(string? ipAddress)
    {
        if (_reader is null || string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out _))
            return null;

        if (_cache.TryGetValue(ipAddress, out GeoIpResult? cached))
            return cached;

        GeoIpResult? result = null;
        try
        {
            if (_reader.TryCity(ipAddress, out var city) && city is not null)
                result = new GeoIpResult(city.Country?.IsoCode, city.City?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[GeoIp] Lookup fail cho {Ip} — fallback null.", ipAddress);
        }

        _cache.Set(ipAddress, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1,
        });
        return result;
    }

    public void Dispose() => _reader?.Dispose();
}
