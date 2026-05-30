using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketApproveCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    public string? ManagerComment { get; set; }

    [JsonIgnore]
    public Guid ManagerId { get; set; }
    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
