using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Command.TicketDeclareIncident;

public class TicketDeclareIncidentCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    public Guid TicketId { get; set; }
    public Guid UserId { get; set; }
    public string? IncidentDescription { get; set; }

    public TicketDeclareIncidentCommand() { }

    public TicketDeclareIncidentCommand(Guid ticketId, Guid userId)
    {
        TicketId = ticketId;
        UserId = userId;
    }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(IncidentDescription))
            response.ListErrors.Add(new Errors { Field = "IncidentDescription", Detail = "Mô tả sự cố không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
