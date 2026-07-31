using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketAssignCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// Staff được Manager chỉ định làm PrimaryHandler — phải đủ tier theo priority của ticket.
    /// </summary>
    public Guid PrimaryHandlerStaffId { get; set; }

    /// <summary>
    /// Danh sách Staff hỗ trợ (Supporter) — không bắt buộc, không giới hạn tier.
    /// </summary>
    public List<Guid> SupporterStaffIds { get; set; } = new();

    public string? Notes { get; set; }

    [JsonIgnore]
    public Guid ManagerId { get; set; }

    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (PrimaryHandlerStaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "PrimaryHandlerStaffId", Detail = "PrimaryHandlerStaffId không hợp lệ." });

        if (SupporterStaffIds.Contains(PrimaryHandlerStaffId))
            response.ListErrors.Add(new Errors { Field = "SupporterStaffIds", Detail = "PrimaryHandler không được đồng thời là Supporter." });

        if (SupporterStaffIds.Count != SupporterStaffIds.Distinct().Count())
            response.ListErrors.Add(new Errors { Field = "SupporterStaffIds", Detail = "Danh sách Supporter không được chứa ID trùng lặp." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
