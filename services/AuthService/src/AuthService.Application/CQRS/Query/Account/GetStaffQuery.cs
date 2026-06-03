using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Query.Account;

public class GetStaffQuery : IRequest<StaffAssignmentProfileListResponse>
{
    public string? Skill { get; set; }
}
