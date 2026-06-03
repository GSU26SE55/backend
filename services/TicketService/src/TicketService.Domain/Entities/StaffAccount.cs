using SharedKernels.Domain;
using TicketService.Domain.Enums;

namespace TicketService.Domain.Entities;

public class StaffAccount : AuditableEntity
{
    public Guid AccountId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeCode { get; set; }
    public AccountStatusEnum Status { get; set; }
    public bool IsAvailable { get; set; } = true;
    public int MaxConcurrentTickets { get; set; } = 10;
    public StaffSkillTierEnum SkillTier { get; set; } = StaffSkillTierEnum.Generalist;
    public List<string> SkillCodes { get; set; } = new();
    public DateTime LastSyncedAt { get; set; }
}
