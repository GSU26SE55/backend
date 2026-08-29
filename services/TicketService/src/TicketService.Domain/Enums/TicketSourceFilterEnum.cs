namespace TicketService.Domain.Enums;

/// <summary>
/// Bộ lọc "nguồn tạo ticket". KHÔNG phải cột thật trong DB —
/// <see cref="TicketOriginEnum"/> mới là cột lưu, nhưng một mình nó không đủ:
/// sự cố môi trường và bảo trì định kỳ đều ghi <c>Origin = System</c>, nên lọc
/// theo Origin thì gộp hai luồng khác hẳn nhau làm một.
///
/// Bốn nguồn người dùng phân biệt trên UI. Xét field chuyên biệt trước rồi mới tới
/// Origin — cùng thứ tự ưu tiên FE dùng để gắn nhãn.
/// </summary>
public enum TicketSourceFilterEnum
{
    /// <summary>Khách tự tạo (Origin = ManualByCustomer).</summary>
    Customer = 1,

    /// <summary>
    /// AI dự đoán ra: sinh từ alert bất thường mà AI module chấm (Origin = AutoFromAlert,
    /// qua TicketBatteryAnomalyDetectedConsumer) hoặc từ điểm cascade risk cao
    /// (Origin = System, không kèm sự cố môi trường / hạn bảo trì).
    /// </summary>
    AiPredicted = 2,

    /// <summary>Sự cố môi trường tại site — nhận diện bằng EnvironmentalIncidentId.</summary>
    Environmental = 3,

    /// <summary>
    /// Bảo trì định kỳ của pin — nhận diện bằng PeriodicMaintenanceDueAtUtc.
    /// KHÔNG dùng PeriodicMaintenanceSourceTicketId: từ khi lịch bảo trì chuyển sang
    /// tầng tài sản, field đó luôn trống nên điều kiện sẽ không bao giờ khớp.
    /// </summary>
    PeriodicMaintenance = 4
}
