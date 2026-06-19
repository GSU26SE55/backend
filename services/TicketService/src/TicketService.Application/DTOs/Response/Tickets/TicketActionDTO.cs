using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketActionDTO
{
    public string Id { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TicketId { get; set; }
    public string Code { get; set; } = string.Empty;
    public TicketStatusEnum Status { get; set; }
}
