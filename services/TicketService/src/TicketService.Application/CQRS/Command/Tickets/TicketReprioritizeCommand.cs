using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketReprioritizeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore] public Guid TicketId { get; set; }

    // Priority KHÔNG nhận trực tiếp từ client — User Guide §3.9: "Mức ưu tiên không do
    // người nhập trực tiếp mà được suy ra từ phạm vi ảnh hưởng và độ khẩn cấp". Handler
    // tính qua IPriorityCalculator, giống bước Triage, để một ticket không thể mang mức
    // ưu tiên mâu thuẫn với Impact/Urgency đang lưu trên chính nó.
    public ImpactScopeEnum Impact { get; set; }
    public UrgencyLevelEnum Urgency { get; set; }
    public string Reason { get; set; } = string.Empty;
    [JsonIgnore] public Guid ManagerId { get; set; }
    [JsonIgnore] public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(TicketId), Detail = "Invalid TicketId." });
        if (!Enum.IsDefined(Impact))
            response.ListErrors.Add(new Errors { Field = nameof(Impact), Detail = "Invalid impact scope." });
        if (!Enum.IsDefined(Urgency))
            response.ListErrors.Add(new Errors { Field = nameof(Urgency), Detail = "Invalid urgency level." });
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length > 1000)
            response.ListErrors.Add(new Errors { Field = nameof(Reason), Detail = "Reason is required and must be at most 1000 characters." });
        if (response.ListErrors.Count > 0)
        { response.IsSuccess = false; response.StatusCode = 400; response.Message = "Invalid input data."; }
        return Task.FromResult(response);
    }
}
