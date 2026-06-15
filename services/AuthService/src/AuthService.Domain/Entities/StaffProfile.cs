using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

public class StaffProfile : AuditableEntity
{
    public Guid AccountId { get; set; }

    public string? EmployeeCode { get; set; }

    public string? Department { get; set; }

    public int MaxConcurrentTickets { get; set; } = 3;

    public bool IsAvailable { get; set; } = true;

    public StaffSkillTierEnum SkillTier { get; set; } = StaffSkillTierEnum.Generalist;

    public string? Notes { get; set; }

    public Account Account { get; set; } = null!;

    public ICollection<StaffSkill> Skills { get; set; } = new List<StaffSkill>();
}
