namespace TicketService.Domain.Enums;

/// <summary>
/// Explains why a ticket is pending. A scheduled assignment is not a hold.
/// </summary>
public enum PendingContextEnum
{
    Scheduled = 1,
    Held = 2
}
