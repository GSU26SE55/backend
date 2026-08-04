using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

/// <summary>
/// Manager gộp ticket B (nghi trùng) vào ticket A (giữ lại). B đóng lại (Closed), link MergedIntoTicketId=A.
/// </summary>
public class TicketMergeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>Ticket B — bị gộp (đóng lại). Từ route.</summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>Ticket A — đích gộp (giữ lại). Từ body.</summary>
    public Guid TargetTicketId { get; set; }

    [JsonIgnore]
    public Guid ManagerId { get; set; }

    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (TargetTicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TargetTicketId", Detail = "TargetTicketId không hợp lệ." });

        if (TicketId != Guid.Empty && TicketId == TargetTicketId)
            response.ListErrors.Add(new Errors { Field = "TargetTicketId", Detail = "Không thể gộp ticket vào chính nó." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
