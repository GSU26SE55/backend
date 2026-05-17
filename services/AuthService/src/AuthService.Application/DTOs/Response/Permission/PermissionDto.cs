using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Permission;

public class PermissionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemPermission { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PermissionListResponse : CommonResponse<List<PermissionDto>> { }

public class RolePermissionsResponse : CommonResponse<List<PermissionDto>> { }

public class PermissionActionResponse : CommonResponse<Guid> { }
