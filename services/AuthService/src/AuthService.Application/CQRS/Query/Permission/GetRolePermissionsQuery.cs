using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Permission;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Application.CQRS.Query.Permission;

/// <summary>Lấy danh sách permission hiện gán cho 1 role.</summary>
public class GetRolePermissionsQuery : IRequest<RolePermissionsResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid RoleId { get; set; }
}
