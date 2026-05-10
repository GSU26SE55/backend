using AuthService.Domain.Enums;

namespace AuthService.Application.DTOs.Response.Role;

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public string? Description { get; set; }
    public RoleStatusEnum Status { get; set; }
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
