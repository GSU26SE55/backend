using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

public class StaffSkill : AuditableEntity
{
    public Guid StaffAccountId { get; set; }

    public string SkillCode { get; set; } = null!;

    public int SkillLevel { get; set; } = 1;

    public DateTime? CertifiedUntil { get; set; }

    public StaffProfile StaffProfile { get; set; } = null!;
}
