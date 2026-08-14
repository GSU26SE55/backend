using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Interfaces.Utils;

public interface ISlaCalculator
{
    DateTime CalculateSlaDueDate(Ticket ticket);

    /// <summary>Sprint Bonus NS-12 (#656) — số giờ SLA theo priority (P1=4h · P2=24h · P3=72h).</summary>
    int GetSlaHours(TicketPriorityEnum priority);

    /// <summary>
    /// Sprint Bonus NS-12 (#656) — DueAt = <paramref name="startedAt"/> + giờ SLA của priority.
    /// Dùng khi tạo timer lúc Assigned + recompute khi Priority đổi (cascade override).
    /// </summary>
    DateTime CalculateDueDate(DateTime startedAt, TicketPriorityEnum priority);
}
