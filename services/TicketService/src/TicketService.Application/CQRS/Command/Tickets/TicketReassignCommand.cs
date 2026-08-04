using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketReassignCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// Staff mới được chỉ định làm PrimaryHandler — phải đủ tier theo priority của ticket.
    /// </summary>
    public Guid NewPrimaryHandlerStaffId { get; set; }

    public string Reason { get; set; } = string.Empty;

    [JsonIgnore]
    public Guid ManagerId { get; set; }

    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (NewPrimaryHandlerStaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "NewPrimaryHandlerStaffId", Detail = "NewPrimaryHandlerStaffId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Reason))
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Lý do điều chuyển không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
