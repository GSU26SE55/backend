using AuthService.Application.DTOs.Response.Account;
using AuthService.Domain.Entities;

namespace AuthService.Application.Mapping;

public static class AccountProfileMapper
{
    private const string DefaultFileDownloadBasePath = "/api/files";

    public static AccountDto ToAccountDto(Account account)
    {
        return new AccountDto
        {
            Id = account.Id,
            Email = account.Email,
            PhoneNumber = account.PhoneNumber,
            FullName = account.FullName,
            AvatarUrl = account.AvatarUrl,
            DateOfBirth = account.DateOfBirth,
            Address = account.Address,
            EmailConfirmed = account.EmailConfirmed,
            PhoneConfirmed = account.PhoneConfirmed,
            TwoFactorEnabled = account.TwoFactorEnabled,
            IsGoogleLinked = account.GoogleId != null,
            Status = account.Status,
            LastLoginAt = account.LastLoginAt,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            RoleId = account.RoleId,
            Role = account.Role?.Name ?? string.Empty,
            RoleAssignedAt = account.RoleAssignedAt,
            RoleAssignedBy = account.RoleAssignedBy,
            Profile = ToProfileDto(account.Profile, account.Id),
            StaffProfile = ToStaffProfileDto(account.StaffProfile),
            DisplayAvatarUrl = ResolveDisplayAvatarUrl(account.Profile)
        };
    }

    public static StaffAssignmentProfileDto ToStaffAssignmentProfileDto(StaffProfile staffProfile)
    {
        return new StaffAssignmentProfileDto
        {
            AccountId = staffProfile.AccountId,
            Email = staffProfile.Account.Email,
            FullName = staffProfile.Account.FullName,
            PhoneNumber = staffProfile.Account.PhoneNumber,
            Department = staffProfile.Department,
            MaxConcurrentTickets = staffProfile.MaxConcurrentTickets,
            IsAvailable = staffProfile.IsAvailable,
            SkillTier = (int)staffProfile.SkillTier,
            DisplayAvatarUrl = ResolveDisplayAvatarUrl(staffProfile.Account.Profile),
            Skills = staffProfile.Skills
                .OrderBy(skill => skill.SkillCode)
                .Select(ToStaffSkillDto)
                .ToList()
        };
    }

    public static AccountProfileDto? ToProfileDto(AccountProfile? profile, Guid accountId)
    {
        if (profile is null)
            return null;

        return new AccountProfileDto
        {
            AccountId = accountId,
            AvatarFileId = profile.AvatarFileId,
            ExternalAvatarUrl = profile.ExternalAvatarUrl,
            AvatarSource = profile.AvatarSource,
            Address = profile.Address,
            BirthDate = profile.BirthDate,
            TimeZone = profile.TimeZone
        };
    }

    public static StaffProfileDto? ToStaffProfileDto(StaffProfile? staffProfile)
    {
        if (staffProfile is null)
            return null;

        return new StaffProfileDto
        {
            AccountId = staffProfile.AccountId,
            EmployeeCode = staffProfile.EmployeeCode,
            Department = staffProfile.Department,
            MaxConcurrentTickets = staffProfile.MaxConcurrentTickets,
            IsAvailable = staffProfile.IsAvailable,
            SkillTier = (int)staffProfile.SkillTier,
            Notes = staffProfile.Notes,
            Skills = staffProfile.Skills
                .OrderBy(skill => skill.SkillCode)
                .Select(ToStaffSkillDto)
                .ToList()
        };
    }

    private static StaffSkillDto ToStaffSkillDto(StaffSkill skill)
    {
        return new StaffSkillDto
        {
            SkillCode = skill.SkillCode,
            SkillLevel = skill.SkillLevel,
            CertifiedUntil = skill.CertifiedUntil
        };
    }

    public static string? ResolveDisplayAvatarUrl(AccountProfile? profile)
    {
        if (profile?.AvatarFileId is Guid fileId)
            return $"{DefaultFileDownloadBasePath}/{fileId}/download";

        return string.IsNullOrWhiteSpace(profile?.ExternalAvatarUrl)
            ? null
            : profile.ExternalAvatarUrl;
    }
}
