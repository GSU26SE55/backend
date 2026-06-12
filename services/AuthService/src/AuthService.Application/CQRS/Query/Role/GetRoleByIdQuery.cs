using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Role;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AuthService.Application.CQRS.Query.Role;

public class GetRoleByIdQuery : IRequest<RoleResponse>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }
}
