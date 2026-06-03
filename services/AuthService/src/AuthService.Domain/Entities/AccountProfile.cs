using AuthService.Domain.Enums;
using SharedKernels.Domain;

namespace AuthService.Domain.Entities;

public class AccountProfile : AuditableEntity
{
    public Guid AccountId { get; set; }

    public Guid? AvatarFileId { get; set; }

    public string? ExternalAvatarUrl { get; set; }

    public AvatarSourceEnum AvatarSource { get; set; } = AvatarSourceEnum.None;

    public string? Address { get; set; }

    public DateTime? BirthDate { get; set; }

    public string? TimeZone { get; set; }

    public Account Account { get; set; } = null!;
}
