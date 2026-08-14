using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

/// <summary>
/// Manager kích hoạt AI kiểm tra lại 1 ticket (chỉ cho ticket Skipped/Pending) —
/// set AiVerifyStatus=Pending + publish lại TicketCreatedEvent để consumer verify lại.
/// </summary>
public class TicketReVerifyCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>Ticket cần verify lại. Từ route.</summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid ManagerId { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
