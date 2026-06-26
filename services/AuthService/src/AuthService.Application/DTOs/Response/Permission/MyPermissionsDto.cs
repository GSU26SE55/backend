using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Permission;

/// <summary>
/// Payload trả về cho endpoint <c>GET /api/auth/me/permissions</c>.
/// Liệt kê toàn bộ permission của role mà account hiện tại đang được gán.
/// </summary>
public class MyPermissionsDto
{
    /// <summary>Guid role mà account đang được gán.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Tên role (vd: "Admin", "Manager", "Staff", "Customer").</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách permission đã sort theo <c>Module</c> rồi <c>Code</c>.
    /// Rỗng nếu role không active hoặc chưa có permission nào được gán.
    /// </summary>
    public List<PermissionDto> Permissions { get; set; } = new();
}

public class MyPermissionsResponse : CommonResponse<MyPermissionsDto> { }
