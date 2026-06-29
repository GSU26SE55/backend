using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.TicketKbReferences;

namespace TicketService.Application.CQRS.Query.TicketKbReferences;

public class GetTicketKbReferencesQuery : IRequest<CommonResponse<List<TicketKbReferenceDTO>>>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public Guid TicketId { get; set; }
}
