using AuthService.Application.DTOs.Response.Role;
using MediatR;

namespace AuthService.Application.CQRS.Command.Role;

public class DeleteRoleCommand : IRequest<RoleActionResponse>
{
    public Guid Id { get; set; }
}
