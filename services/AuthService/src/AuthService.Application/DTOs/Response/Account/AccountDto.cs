using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Account;

public class AccountDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string FullName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Address { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public AccountStatusEnum Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<string> Roles { get; set; } = new();
}
