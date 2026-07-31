namespace AuthService.Application.DTOs.Response.Account;

public class StaffProfileDto
{
    public Guid AccountId { get; set; }

    public string? EmployeeCode { get; set; }

    public string? Department { get; set; }

    public int MaxConcurrentTickets { get; set; }

    public bool IsAvailable { get; set; }

    public int SkillTier { get; set; }

    public string? Notes { get; set; }

    public List<StaffSkillDto> Skills { get; set; } = new();
}
