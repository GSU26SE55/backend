using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketReopenCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    public string ReopenReason { get; set; } = string.Empty;

    [JsonIgnore]
    public Guid CustomerId { get; set; }

    [JsonIgnore]
    public string? CustomerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(ReopenReason))
            response.ListErrors.Add(new Errors { Field = "ReopenReason", Detail = "Lý do mở lại không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
