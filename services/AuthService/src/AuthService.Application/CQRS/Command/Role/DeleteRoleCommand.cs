using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Role;
using MediatR;

namespace AuthService.Application.CQRS.Command.Role;

public class DeleteRoleCommand : IRequest<RoleActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
}
