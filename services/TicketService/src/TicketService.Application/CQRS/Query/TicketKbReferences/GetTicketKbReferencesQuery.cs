using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.TicketKbReferences;

namespace TicketService.Application.CQRS.Query.TicketKbReferences;

public class GetTicketKbReferencesQuery : IRequest<CommonResponse<List<TicketKbReferenceDTO>>>
{
    public Guid TicketId { get; set; }
}
