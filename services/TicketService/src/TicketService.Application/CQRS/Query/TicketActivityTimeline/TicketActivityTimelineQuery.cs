using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;

namespace TicketService.Application.CQRS.Query.TicketActivityTimeline;

public class TicketActivityTimelineQuery : IRequest<CommonResponse<List<TicketActivityDTO>>>
{
    public Guid TicketId { get; set; }
    public Guid? ActorUserId { get; set; }
    public IReadOnlyCollection<string> ActorRoles { get; set; } = Array.Empty<string>();
}
