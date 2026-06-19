namespace AuthService.Application.DTOs.Response.TrustedDevice;

/// <summary>
/// #AUTH-48: Display info cho trusted device trong list endpoint.
/// </summary>
public class TrustedDeviceDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string IpPrefix { get; set; } = string.Empty;
    public string? UserAgentSnapshot { get; set; }
    public DateTime TrustedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public bool IsCurrentDevice { get; set; }
}
