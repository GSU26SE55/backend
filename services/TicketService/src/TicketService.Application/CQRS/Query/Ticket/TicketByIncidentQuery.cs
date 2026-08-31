using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.Ticket;

/// <summary>
/// Ticket auto-tạo từ một environmental incident. Data = null nếu chưa có ticket nào.
///
/// Tra ngược quan hệ một chiều Ticket → EnvironmentalIncident; xem TicketsController.ByIncident.
/// </summary>
public class TicketByIncidentQuery : IRequest<CommonResponse<TicketDTO?>>
{
    public Guid EnvironmentalIncidentId { get; set; }
}
