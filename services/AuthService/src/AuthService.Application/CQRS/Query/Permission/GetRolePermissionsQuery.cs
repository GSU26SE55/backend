using AuthService.Application.DTOs.Response.Permission;
using MediatR;

namespace AuthService.Application.CQRS.Query.Permission;

/// <summary>Lấy danh sách permission hiện gán cho 1 role.</summary>
public class GetRolePermissionsQuery : IRequest<RolePermissionsResponse>
{
    public Guid RoleId { get; set; }
}
