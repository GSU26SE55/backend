using SharedContracts.Events.Root;

namespace SharedContracts.Events;

public enum PeriodicMaintenanceReminderStage
{
    CustomerFirstReminder = 1,
    CustomerSecondReminder = 2,
    ManagerEscalation = 3
}

public record PeriodicMaintenanceReminderDueEvent(
    Guid TicketId,
    string Code,
    Guid BatteryAssetId,
    Guid CustomerId,
    DateTime MaintenanceDueAtUtc,
    DateTime ScheduleDeadlineAtUtc,
    PeriodicMaintenanceReminderStage Stage,
    bool IsOverdue
) : IntegrationEvent;

public record PeriodicMaintenanceScheduleChangedEvent(
    Guid TicketId,
    string Code,
    Guid BatteryAssetId,
    Guid CustomerId,
    DateTime? PreviousScheduledStartAtUtc,
    DateTime ScheduledStartAtUtc,
    int ScheduleVersion,
    string ChangedByRole,
    Guid ChangedByUserId,
    string? Reason,
    DateTime MaintenanceDueAtUtc,
    bool IsOverdue
) : IntegrationEvent;

/// <summary>
/// BatteryService đã ghi một kỳ bảo trì tới hạn cho một cục pin.
/// </summary>
/// <remarks>
/// <para>
/// Sự kiện này nối lại nửa còn thiếu của cuộc chuyển lịch bảo trì sang tầng tài sản. Lịch
/// nay thuộc về <c>BatteryAsset.NextMaintenanceDueAtUtc</c> — đúng chỗ, và vá được ba lỗi
/// của cách cũ: pin chưa từng có ticket Closed không bao giờ vào lịch, mọi ticket đóng đều
/// dời chu kỳ, và không thể hỏi "pin nào sắp tới hạn" nếu không quét bảng ticket.
/// </para>
/// <para>
/// Nhưng ghi nhật ký thôi thì không ai được báo và không ai được cử đi. TicketService nhận
/// sự kiện này để mở ticket bảo trì — nhờ đó công việc quay lại hàng chờ của Manager và
/// thừa hưởng SLA, phân công, chat, nhật ký hoạt động sẵn có, thay vì phải dựng lại.
/// </para>
/// <para>
/// Đi theo đúng khuôn <see cref="BatteryAnomalyDetectedEvent"/>: BatteryService phát,
/// TicketService tiêu thụ và tạo ticket.
/// </para>
/// </remarks>
/// <param name="BatteryAssetId">Pin tới kỳ.</param>
/// <param name="CustomerId">Chủ sở hữu pin — người sẽ được nhắc.</param>
/// <param name="SerialNumber">Số sê-ri, hiển thị trên ticket.</param>
/// <param name="MaintenanceCycleId">Dòng nhật ký kỳ này, để truy ngược từ ticket.</param>
/// <param name="CycleNo">Số thứ tự kỳ, 1 là kỳ đầu kể từ lúc lắp đặt.</param>
/// <param name="DueAtUtc">Hạn theo kế hoạch của kỳ — mốc chống trùng cùng BatteryAssetId.</param>
/// <param name="IntervalMonths">Chu kỳ của loại pin này, dùng cho mô tả ticket.</param>
public record MaintenanceCycleDueEvent(
    Guid BatteryAssetId,
    Guid CustomerId,
    string? SerialNumber,
    Guid MaintenanceCycleId,
    int CycleNo,
    DateTime DueAtUtc,
    int IntervalMonths
) : IntegrationEvent;
