using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public record SlaBreachedEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime BreachedAt { get; init; }
    public string Priority { get; init; } = string.Empty;

    /// <summary>
    /// Mã ticket hiển thị cho người đọc (vd <c>TKT-2602-0001</c>). Thêm 03/08/2026.
    ///
    /// <para>Trước đó payload chỉ có <c>TicketId</c> — một GUID. Thông báo vỡ SLA vì thế **không nhắc
    /// được ticket nào**, chỉ nói chung chung "Ticket mức P1 đã quá hạn", trong khi đây đúng là loại
    /// thông báo cần biết ngay ticket nào để mở ra xử lý. Rỗng = event cũ còn trong hàng đợi.</para>
    /// </summary>
    public string Code { get; init; } = string.Empty;
}

public record SlaWarningEvent : IntegrationEvent
{
    public Guid TicketId { get; init; }
    public DateTime WarningAt { get; init; }
    public double Percentage { get; init; }

    /// <summary>Mã ticket hiển thị — xem chú thích cùng tên ở <see cref="SlaBreachedEvent"/>.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Sprint 6.2 NOTI-05 (#676) — Staff đang được assign ticket. Spec §3.4 yêu cầu SLA warning
    /// báo cả Staff phụ trách lẫn Manager; trước đó payload không có StaffId nên consumer chỉ
    /// broadcast Manager được (reviewnotification.md §4.2). Null = ticket chưa assign ai.
    /// </summary>
    public Guid? StaffId { get; init; }
}
