namespace TicketService.Application.DTOs.Response.TicketKbReferences;

public class TicketKBRefActionDTO
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    public string TicketId { get; set; } = string.Empty;
    public string KbId { get; set; } = string.Empty;
}
