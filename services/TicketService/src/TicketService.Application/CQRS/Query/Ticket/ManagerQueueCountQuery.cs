using MediatR;
using SharedContracts.Common.Responses;

namespace TicketService.Application.CQRS.Query.Ticket;

/// <summary>
/// Returns the number of tickets awaiting Manager triage.
/// </summary>
public sealed class ManagerQueueCountQuery : IRequest<CommonResponse<int>>
{
}
