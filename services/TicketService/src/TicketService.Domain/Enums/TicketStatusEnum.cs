namespace TicketService.Domain.Enums;

/// <summary>Canonical GH-1176 ticket lifecycle.</summary>
public enum TicketStatusEnum
{
    Open = 1,
    Pending = 2,
    InProgress = 3,
    Request = 4,
    ReAssign = 5,
    Completed = 6,
    Closed = 7,
    ClosedRejected = 8
}
