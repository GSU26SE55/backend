namespace TicketService.Domain.Enums;

/// <summary>
/// Mức độ ưu tiên của Ticket.
/// </summary>
public enum TicketPriorityEnum
{
    /// <summary>Nghiêm trọng (SLA ngắn nhất).</summary>
    P1Critical = 1,
    /// <summary>Cao.</summary>
    P2High = 2,
    /// <summary>Bình thường.</summary>
    P3Normal = 3
}
