using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Login;

public class LoginAttemptDto
{
    public string Id { get; set; } = string.Empty;
    public Guid? AccountId { get; set; }
    public string AttemptedEmail { get; set; } = string.Empty;
    public LoginAttemptResult Result { get; set; }
    public string ResultName { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
