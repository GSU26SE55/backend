using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Account;

public class AccountProfileDto
{
    public Guid AccountId { get; set; }

    public Guid? AvatarFileId { get; set; }

    public string? ExternalAvatarUrl { get; set; }

    public AvatarSourceEnum AvatarSource { get; set; }

    public string? Address { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? TimeZone { get; set; }
}
