using AuthService.Application.DTOs.Response.Account;
using MediatR;

namespace AuthService.Application.CQRS.Query.Account;

public class GetStaffQuery : IRequest<StaffAssignmentProfileListResponse>
{
    public string? Skill { get; set; }

    /// <summary>
    /// Optional ticket priority used to return staff eligible as the primary handler.
    /// Accepted values: P1Critical, P2High, P3Normal.
    /// </summary>
    public string? TicketPriority { get; set; }
}
