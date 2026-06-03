namespace TicketService.Domain.Enums;

public enum ActivityActionEnum
{
    Created = 1,
    StatusChanged = 2,
    PriorityAssigned = 3,
    StaffAssigned = 4,
    StaffReassigned = 5,
    Commented = 6,
    MaintenanceLogged = 7,
    AttachmentAdded = 8,
    SlaPaused = 9,
    SlaResumed = 10,
    SlaWarning = 11,
    SlaBreached = 12,
    EscalationRequested = 13,
    Escalated = 14,
    IncidentDeclared = 15,
    Resolved = 16,
    Approved = 17,
    Rejected = 18,
    Rated = 19,
    Reopened = 20,
    Closed = 21,
    AutoClosed = 22,
    ResolvedByEscalatedStaff = 23,
    TriageApproved = 24
}
