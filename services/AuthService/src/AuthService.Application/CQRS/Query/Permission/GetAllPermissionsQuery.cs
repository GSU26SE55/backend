using AuthService.Application.DTOs.Response.Permission;
using MediatR;

namespace AuthService.Application.CQRS.Query.Permission;

/// <summary>
/// Trả về tất cả permission trong hệ thống (cả system + custom). Có filter module tùy chọn.
/// </summary>
public class GetAllPermissionsQuery : IRequest<PermissionListResponse>
{
    public string? Module { get; set; }
}
