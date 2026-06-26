namespace AuditAggregatorService.Application.Interfaces;

/// <summary>Kết quả tra cứu geo IP (Sprint audit <c>#AUDIT-16</c>).</summary>
public record GeoIpResult(string? CountryCode, string? City);

/// <summary>
/// Tra cứu địa lý từ IP (Sprint audit <c>#AUDIT-16</c>) — MaxMind GeoLite2 (D11).
/// Implementation phải cache (LRU) + fallback <c>null</c> khi không tra được (enrichment optional, không chặn pipeline).
/// </summary>
public interface IGeoIpResolver
{
    /// <summary>Tra country/city từ IP. Trả null nếu IP rỗng/không hợp lệ/không có DB.</summary>
    GeoIpResult? Lookup(string? ipAddress);
}
