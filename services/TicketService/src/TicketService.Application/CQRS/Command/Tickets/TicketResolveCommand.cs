using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Ticket;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketResolveCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    public string ResolutionSummary { get; set; } = string.Empty;

    [JsonIgnore]
    public Guid StaffId { get; set; }
    [JsonIgnore]
    public string? StaffName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(ResolutionSummary))
            response.ListErrors.Add(new Errors { Field = "ResolutionSummary", Detail = "Tổng kết xử lý không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
