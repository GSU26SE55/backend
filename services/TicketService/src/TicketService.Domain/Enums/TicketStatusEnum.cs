namespace TicketService.Domain.Enums;

public enum TicketStatusEnum
{
    New = 1,
    Open = 2,
    Assigned = 3,
    InProgress = 4,
    WaitingCustomer = 5,
    WaitingParts = 6,
    WaitingOnsiteSchedule = 7,
    Resolved = 8,
    Escalated = 9,
    ClosedPendingRate = 10,
    Closed = 11,
    ClosedRejected = 12,
    Incident = 13
}
