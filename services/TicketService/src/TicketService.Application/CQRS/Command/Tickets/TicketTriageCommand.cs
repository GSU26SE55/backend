using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketTriageCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// Mức độ tác động (1-4).
    /// </summary>
    public ImpactScopeEnum Impact { get; set; }
    public UrgencyLevelEnum Urgency { get; set; }
    public TicketPriorityEnum? ManualPriority { get; set; }
    /// <summary>
    /// Priority override reason.
    /// </summary>
    public string? PriorityOverrideReason { get; set; }
    public string? ManagerComment { get; set; }

    /// <summary>
    /// ID của Manager.
    /// </summary>
    [JsonIgnore]
    public Guid ManagerId { get; set; }
    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (!Enum.IsDefined(typeof(ImpactScopeEnum), Impact))
            response.ListErrors.Add(new Errors { Field = "Impact", Detail = "ImpactScope không hợp lệ." });

        if (!Enum.IsDefined(typeof(UrgencyLevelEnum), Urgency))
            response.ListErrors.Add(new Errors { Field = "Urgency", Detail = "UrgencyLevel không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
