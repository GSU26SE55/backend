using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.RefreshToken;

public class SessionDto
{
    public Guid Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiredAt { get; set; }
    public RefreshTokenStatus Status { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public bool IsCurrent { get; set; }
}
