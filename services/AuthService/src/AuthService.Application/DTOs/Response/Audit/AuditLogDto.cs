using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Audit;

public class AuditLogDto
{
    public string Id { get; set; } = string.Empty;
    public AuditActionEnum Action { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public Guid? TargetAccountId { get; set; }
    public string? TargetEmail { get; set; }
    public Guid? ActorAccountId { get; set; }
    public bool IsSuccess { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedAt { get; set; }
}
