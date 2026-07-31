namespace AuthService.Application.DTOs.Response.Account;

public class StaffAssignmentProfileDto
{
    public Guid AccountId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Department { get; set; }

    public int MaxConcurrentTickets { get; set; }

    public bool IsAvailable { get; set; }

    public int SkillTier { get; set; }

    public string? DisplayAvatarUrl { get; set; }

    public List<StaffSkillDto> Skills { get; set; } = new();
}
