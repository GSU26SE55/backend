using AuthService.Application.DTOs.Response.Role;
using MediatR;

namespace AuthService.Application.CQRS.Query.Role;

public class GetRoleByIdQuery : IRequest<RoleResponse>
{
    public Guid Id { get; set; }
}
