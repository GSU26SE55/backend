namespace AuthService.Application.DTOs.Response.Account;

public class StaffSkillDto
{
    public string SkillCode { get; set; } = string.Empty;

    public int SkillLevel { get; set; }

    public DateTime? CertifiedUntil { get; set; }
}
