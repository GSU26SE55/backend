using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketActivityDTO
{
    /// <summary>
    /// Id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    public string TicketId { get; set; } = string.Empty;
    public string? SourceTicketId { get; set; }
    public string? ActorUserId { get; set; }
    /// <summary>
    /// Actor role.
    /// </summary>
    public ActorRoleEnum ActorRole { get; set; }
    public string? ActorDisplayName { get; set; }
    public ActivityActionEnum Action { get; set; }
    /// <summary>
    /// Old value.
    /// </summary>
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    /// <summary>
    /// Thời gian tạo (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
