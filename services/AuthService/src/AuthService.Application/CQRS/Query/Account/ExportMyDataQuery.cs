using MediatR;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Query.Account;

/// <summary>
/// #AUTH-62: GDPR Article 20 data portability — user export toàn bộ data của mình
/// dưới dạng JSON structured.
/// </summary>
public class ExportMyDataQuery : IRequest<CommonResponse<AccountDataExportDto>>
{
    public Guid AccountId { get; set; }
}

public class AccountDataExportDto
{
    public AccountSnapshot Account { get; set; } = new();
    public AccountProfileSnapshot? Profile { get; set; }
    public StaffProfileSnapshot? StaffProfile { get; set; }
    public List<SessionSnapshot> Sessions { get; set; } = new();
    public List<AuditLogSnapshot> AuditLogs { get; set; } = new();
    public List<BackupCodeSnapshot> BackupCodes { get; set; } = new();
    public DateTime ExportedAt { get; set; }
    public string Format { get; set; } = "json";
    public string Version { get; set; } = "1.0";
}

public class AccountSnapshot
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? GoogleId { get; set; }
    public string? Provider { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AccountProfileSnapshot
{
    public string? ExternalAvatarUrl { get; set; }
    public string? AvatarSource { get; set; }
    public string? Address { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? TimeZone { get; set; }
}

public class StaffProfileSnapshot
{
    public string? EmployeeCode { get; set; }
    public string? Department { get; set; }
    public string? SkillTier { get; set; }
    public string? Notes { get; set; }
}

public class SessionSnapshot
{
    public Guid Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiredAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? DeviceId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

public class AuditLogSnapshot
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public bool IsSuccess { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Reason { get; set; }
}

public class BackupCodeSnapshot
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
}
