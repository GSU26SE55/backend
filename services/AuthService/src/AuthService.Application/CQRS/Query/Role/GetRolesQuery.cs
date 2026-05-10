using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;

namespace AuthService.Application.CQRS.Query.Role;

public class GetRolesQuery : PaginationRequest, IRequest<RoleListResponse>
{
    public string? Keyword { get; set; }
    public RoleStatusEnum? Status { get; set; }
    public bool? IsSystemRole { get; set; }
}
