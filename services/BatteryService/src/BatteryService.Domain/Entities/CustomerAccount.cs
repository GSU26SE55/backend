using SharedKernels.Domain;

namespace BatteryService.Domain.Entities;

public class CustomerAccount : AuditableEntity
{
    public string Email { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string RolesCsv { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime LastSyncedAtUtc { get; set; }
}
