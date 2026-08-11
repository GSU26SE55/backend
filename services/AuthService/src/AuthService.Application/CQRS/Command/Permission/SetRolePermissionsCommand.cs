using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Permission;

/// <summary>
/// Set toàn bộ permission cho 1 role (replace semantics): xóa các permission không có trong list,
/// thêm các permission mới. Permissions không trong PermissionIds sẽ bị soft-delete trong role_permissions.
///
/// Không cho phép thao tác với system role trừ khi <see cref="AllowSystemRole"/>=true (chỉ Admin set).
/// </summary>
public class SetRolePermissionsCommand : IRequest<PermissionActionResponse>, IValidatable<PermissionActionResponse>
{
    /// <summary>Set bởi controller từ route.</summary>
    [JsonIgnore]
    public Guid RoleId { get; set; }

    public List<Guid> PermissionIds { get; set; } = new();

    /// <summary>Khi true, cho phép modify system role (Admin/Manager/Staff/Customer).</summary>
    public bool AllowSystemRole { get; set; } = false;

    public Task<PermissionActionResponse> ValidateAsync()
    {
        var response = new PermissionActionResponse();

        if (PermissionIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "PermissionIds", Detail = "PermissionIds contains an invalid value." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
