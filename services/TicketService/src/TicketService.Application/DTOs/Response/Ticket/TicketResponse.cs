using SharedContracts.Common.Responses;
using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Ticket;

public class TicketDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketStatusEnum Status { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public TicketCategoryEnum Category { get; set; }
    public TicketOriginEnum Origin { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? AssignedStaffId { get; set; }
    public string? AssignedStaffName { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public EscalationReasonEnum? EscalationReason { get; set; }
}

public class TicketActionDto
{
    public string Id { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TicketId { get; set; }
    public string Code { get; set; } = string.Empty;
    public TicketStatusEnum Status { get; set; }
}

public class TicketActionResponse : CommonResponse<TicketActionDto> { }
